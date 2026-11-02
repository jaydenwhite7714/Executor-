--!strict
--[[
    DrivenTestEngineBridge.server.lua
    ----------------------------------------------------------------------
    Place in: ServerScriptService (Script, RunContext = Server).

    Purpose: receive run commands for YOUR OWN game (Driven Football), execute them inside the
    private test session, and report the measurement back to your testing service.

    Requirements on the game:
      * Game Settings -> Security -> "Allow HTTP Requests" = true (for result delivery).
      * This script must never be replicated to clients: keep it in ServerScriptService.

    Configure the two constants below, then implement the handlers your game supports.
    Handlers that are not implemented answer `rejected` — never a fabricated result.
]]

local HttpService = game:GetService("HttpService")
local MessagingService = game:GetService("MessagingService")
local Players = game:GetService("Players")

local CONFIG = {
    -- Topic your testing service publishes commands to (Open Cloud messaging-service API).
    COMMAND_TOPIC = "dte-test-runs",

    -- Endpoint on YOUR testing service that accepts finished runs.
    RESULT_ENDPOINT = "https://dte.example.com/internal/game/results",

    -- Shared secret for the game -> service hop. In production, load this from a private store
    -- (e.g. a private DataStore entry readable only by the server) rather than hard-coding it.
    BRIDGE_TOKEN = "replace-with-a-long-random-string",

    -- Only instrument this avatar. Set to the developer account's UserId.
    ALLOWED_USER_ID = 1234567,

    -- Maximum runs processed per minute by this server.
    MAX_RUNS_PER_MINUTE = 6,
}

local startedAtByRun: { [string]: number } = {}
local runsThisMinute = 0
local minuteWindowStart = os.clock()
local processedRuns: { [string]: boolean } = {}

--------------------------------------------------------------------------------
-- helpers
--------------------------------------------------------------------------------

local function nowIso(): string
    return os.date("!%Y-%m-%dT%H:%M:%SZ")
end

local function log(level: string, message: string, detail: string?)
    -- Keep this server-only; it doubles as the anti-cheat feed you can forward to your service.
    print(string.format("[DTE][%s] %s%s", level, message, detail and (" | " .. detail) or ""))
end

local function rateLimited(): boolean
    if os.clock() - minuteWindowStart > 60 then
        minuteWindowStart = os.clock()
        runsThisMinute = 0
    end
    return runsThisMinute >= CONFIG.MAX_RUNS_PER_MINUTE
end

-- Delivers a finished run to the testing service with retry/backoff.
local function postResult(payload: { [string]: any })
    task.spawn(function()
        payload.gameServerId = game.JobId
        payload.placeId = tostring(game.PlaceId)
        local body = HttpService:JSONEncode(payload)

        for attempt = 1, 4 do
            local ok, response = pcall(function()
                return HttpService:RequestAsync({
                    Url = CONFIG.RESULT_ENDPOINT,
                    Method = "POST",
                    Headers = {
                        ["Content-Type"] = "application/json",
                        ["Authorization"] = "Bearer " .. CONFIG.BRIDGE_TOKEN,
                    },
                    Body = body,
                })
            end)

            if ok and response.Success then
                return
            end

            log("warn", "result delivery failed", string.format("attempt %d runId=%s", attempt, tostring(payload.runId)))
            task.wait(2 ^ attempt)
        end

        log("error", "giving up on result delivery", tostring(payload.runId))
    end)
end

local function rejected(runId: string, testId: string, message: string)
    postResult({
        runId = runId,
        testId = testId,
        state = "rejected",
        message = message,
        startedAt = nowIso(),
        completedAt = nowIso(),
        checks = {},
    })
end

local function findTargetCharacter(): (Model?, Player?)
    for _, player in ipairs(Players:GetPlayers()) do
        if player.UserId == CONFIG.ALLOWED_USER_ID then
            local character = player.Character
            if character then
                return character, player
            end
        end
    end
    return nil, nil
end

--------------------------------------------------------------------------------
-- handlers — real measurements from the server's point of view
--------------------------------------------------------------------------------

local handlers: { [string]: (runId: string, params: { [string]: any }) -> () } = {}

-- speed: samples the character's assembly velocity for N seconds and reports peak + average.
handlers.speed = function(runId, params)
    local duration = math.clamp(tonumber(params.durationSeconds) or 4, 1, 10)
    local character, player = findTargetCharacter()
    if not character or not player then
        return rejected(runId, "speed", "The reserved test avatar is not in this server.")
    end

    local root = character:FindFirstChild("HumanoidRootPart") :: BasePart?
    local humanoid = character:FindFirstChildOfClass("Humanoid")
    if not root or not humanoid then
        return rejected(runId, "speed", "Avatar has no HumanoidRootPart/Humanoid.")
    end

    task.spawn(function()
        local startedAt = nowIso()
        local samples = {}
        local peak = 0
        local accel = 0
        local finish = os.clock() + duration
        local previous = 0

        while os.clock() < finish do
            local magnitude = root.AssemblyLinearVelocity.Magnitude
            table.insert(samples, magnitude)
            if magnitude > peak then
                peak = magnitude
            end
            if magnitude > 1 and previous == 0 then
                accel = magnitude -- first non-zero sample: coarse acceleration proxy
            end
            previous = magnitude
            task.wait(0.05)
        end

        if #samples == 0 then
            return rejected(runId, "speed", "No samples collected.")
        end

        local sum = 0
        for _, value in ipairs(samples) do
            sum += value
        end
        local average = sum / #samples

        postResult({
            runId = runId,
            testId = "speed",
            state = peak > 0 and "passed" or "failed",
            metric = math.floor(peak * 100) / 100,
            unit = "studs/s",
            threshold = 0,
            message = string.format("peak %.2f · avg %.2f · %d samples", peak, average, #samples),
            startedAt = startedAt,
            completedAt = nowIso(),
            checks = {
                { name = "developer_account", passed = true, detail = tostring(player.UserId) },
                { name = "sample_count", passed = #samples >= duration * 10, detail = tostring(#samples) },
            },
            evidence = { average = string.format("%.2f", average), firstSample = string.format("%.2f", accel) },
        })
    end)
end

-- jump: tracks Humanoid state transitions and the Y coordinate across N attempts.
handlers.jump = function(runId, params)
    local attempts = math.clamp(tonumber(params.attempts) or 3, 1, 5)
    local character, player = findTargetCharacter()
    if not character or not player then
        return rejected(runId, "jump", "The reserved test avatar is not in this server.")
    end

    local root = character:FindFirstChild("HumanoidRootPart") :: BasePart?
    local humanoid = character:FindFirstChildOfClass("Humanoid")
    if not root or not humanoid then
        return rejected(runId, "jump", "Avatar has no HumanoidRootPart/Humanoid.")
    end

    task.spawn(function()
        local startedAt = nowIso()
        local apexHeights = {}
        local airtimes = {}
        local cooldowns = {}

        for attempt = 1, attempts do
            local groundY = root.Position.Y
            local launched = 0
            local lastJump = 0

            local connection
            connection = humanoid.StateChanged:Connect(function(_, newState)
                if newState == Enum.HumanoidStateType.Jumping then
                    launched = os.clock()
                elseif newState == Enum.HumanoidStateType.Landed and launched > 0 then
                    local airtime = os.clock() - launched
                    table.insert(airtimes, airtime)
                    table.insert(apexHeights, math.max(0, root.Position.Y - groundY))
                    if lastJump > 0 then
                        table.insert(cooldowns, launched - lastJump)
                    end
                    lastJump = launched
                    launched = 0
                end
            end)

            -- Let the avatar settle, then ask it to jump.
            task.wait(0.6)
            humanoid:ChangeState(Enum.HumanoidStateType.Jumping)
            task.wait(2.2)
            connection:Disconnect()
        end

        if #airtimes == 0 then
            return rejected(runId, "jump", "No jump was registered. Drive the avatar from the game side.")
        end

        local peak = 0
        for _, height in ipairs(apexHeights) do
            if height > peak then
                peak = height
            end
        end

        postResult({
            runId = runId,
            testId = "jump",
            state = peak > 0 and "passed" or "failed",
            metric = math.floor(peak * 100) / 100,
            unit = "studs",
            message = string.format("%d jump(s) observed", #airtimes),
            startedAt = startedAt,
            completedAt = nowIso(),
            checks = {
                { name = "developer_account", passed = true, detail = tostring(player.UserId) },
                { name = "attempts_observed", passed = #airtimes > 0, detail = tostring(#airtimes) },
            },
            evidence = { airtimes = table.concat(airtimes, ",") },
        })
    end)
end

-- These two need hooks into your own Driven Football mechanics (catch windows, dime drill
-- scoring). Until you expose them, answer honestly instead of inventing a result.
handlers["auto-catch"] = function(runId, _params)
    rejected(runId, "auto-catch", "Not wired: expose your catch-window system to this bridge.")
end

handlers["dime-accuracy"] = function(runId, _params)
    rejected(runId, "dime-accuracy", "Not wired: expose your dime drill scoring to this bridge.")
end

--------------------------------------------------------------------------------
-- command intake
--------------------------------------------------------------------------------

local function handleCommand(raw: string)
    local ok, command = pcall(function()
        return HttpService:JSONDecode(raw)
    end)

    if not ok or type(command) ~= "table" then
        log("warn", "discarded malformed command")
        return
    end

    local runId = command.runId
    local testId = command.testId
    if type(runId) ~= "string" or type(testId) ~= "string" then
        log("warn", "discarded command without runId/testId")
        return
    end

    if processedRuns[runId] then
        log("warn", "duplicate runId ignored", runId)
        return
    end
    processedRuns[runId] = true

    -- Only the reserved avatar may be tested.
    local _, player = findTargetCharacter()
    if not player then
        return rejected(runId, testId, "The reserved developer avatar is not in this server.")
    end

    if rateLimited() then
        return rejected(runId, testId, "Bridge rate limit reached on this server.")
    end
    runsThisMinute += 1

    local handler = handlers[testId]
    if not handler then
        return rejected(runId, testId, "This build has no handler for '" .. testId .. "'.")
    end

    startedAtByRun[runId] = os.clock()
    local params = type(command.parameters) == "table" and command.parameters or {}

    local handlerOk, err = pcall(handler, runId, params)
    if not handlerOk then
        log("error", "handler crashed", tostring(err))
        rejected(runId, testId, "Handler error: " .. tostring(err))
    end
end

local subscribed = false
local function subscribe()
    while not subscribed do
        local ok, err = pcall(function()
            MessagingService:SubscribeAsync(CONFIG.COMMAND_TOPIC, function(message)
                handleCommand(message.Data)
            end)
            subscribed = true
        end)

        if not ok then
            log("error", "subscription failed, retrying", tostring(err))
            task.wait(10)
        end
    end

    log("info", "bridge online", "place " .. tostring(game.PlaceId) .. " · topic " .. CONFIG.COMMAND_TOPIC)
end

subscribe()
