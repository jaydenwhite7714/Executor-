# Wiring Driven Test Engine to your own game (Driven Football)

The overlay **never** injects into, hooks, or reads the Roblox client. It sends signed requests to
your testing service; your service asks your own game server to measure something; the game server
reports back; the service returns that result to the overlay.

That is the only path in this design, and it is why every number in the UI is server-authoritative.

```
┌────────────────────┐   signed HTTPS    ┌──────────────────────┐   Open Cloud       ┌────────────────────┐
│  DTE overlay (PC)  │ ────────────────► │  Your testing        │ ─────────────────► │  Roblox game       │
│  HMAC + session    │ ◄──────────────── │  service             │ ◄───────────────── │  server (your      │
│                    │   JSON results    │  (protocol v1)       │   HTTPS results    │  private session)  │
└────────────────────┘                   └──────────────────────┘                    └────────────────────┘
```

## 1. Why the game needs a service in the middle

A Roblox game server cannot accept inbound HTTP requests and Roblox does not expose a public
webhook into a running server. Something outside Roblox must:

1. hold the developer key and authenticate the desktop client (protocol v1 in
   [`PROTOCOL.md`](PROTOCOL.md));
2. deliver a run request into the specific private test server;
3. receive the result and keep it until the overlay polls for it.

`server/` is a reference implementation of steps 1 and 3 (authentication, sessions, log streaming,
anti-cheat channel). Step 2 — actually running the test — is intentionally **not** implemented
there, because a result may only come from your game.

## 2. Two supported transports to the game

### A. Open Cloud `MessagingService` (recommended for run delivery)

* Publish a command from your service:

  ```
  POST https://apis.roblox.com/messaging-service/v1/universes/{universeId}/topics/dte-test-runs
  x-api-key: <Open Cloud API key with "messaging-service:write" scope>

  { "runId": "...", "testId": "speed", "parameters": { "durationSeconds": 4 } }
  ```

* The game subscribes:

  ```lua
  game:GetService("MessagingService"):SubscribeAsync("dte-test-runs", function(message)
      -- validate + start the run
  end)
  ```

* Restrict the API key to the universe and the messaging scope, keep it only on the service, and
  never ship it inside the game.

### B. `HttpService` (recommended for results)

Roblox servers can make **outbound** HTTP requests when `HttpService.HttpEnabled` is on. Your
service exposes a hook such as `POST /internal/game/results` protected by a per-server token, and
the game posts each finished run there. `roblox/DrivenTestEngineBridge.server.lua` shows this path
end to end, including retry with backoff.

DataStore or MemoryStore is also viable for results: the game writes, the service reads with the
Open Cloud DataStore API. Use whichever your infrastructure already has.

## 3. Server-side validation checklist

Do all of this in your service **and** again inside the game. The desktop client repeats a subset
of it only to avoid pointless traffic.

| Check | Where | Failure |
| --- | --- | --- |
| Signature valid, timestamp within 300 s, nonce unused | service | `401 unauthorized` / `clock_skew` / `replay_detected` |
| Key id belongs to the developer account allow-list | service | `403 developer_only` |
| Session is the requested **private** test session (reserved job id) | service + game | `403 private_session_required` |
| Requested test id is enabled for this session | service + game | `422 unknown_test` |
| Parameters inside the documented range | service + game | `422 validation_failed` |
| Only the reserved avatar is instrumented | game | `rejected` run state |
| Duplicate `runId` processed once | game | ignore |
| Rate limit per key id | service | `429 rate_limited` |

## 4. What the game must return

Every run ends with one of `passed | failed | errored | rejected | cancelled | expired` plus the
measurement. The overlay shows exactly what it receives: no metric, no pass, no invention.

```lua
{
    runId = "...",
    state = "passed",
    metric = 34.7,
    unit = "studs/s",
    threshold = 32,
    message = "Peak sprint velocity sampled over 4s",
    checks = { { name = "developer_account", passed = true, detail = "1234567" } },
    startedAt = os.date("!%Y-%m-%dT%H:%M:%SZ"),
    completedAt = os.date("!%Y-%m-%dT%H:%M:%SZ"),
}
```

## 5. Test ids

| id | Meaning | Reference handler |
| --- | --- | --- |
| `speed` | Sprint velocity / acceleration ramp | implemented in the example script (samples `AssemblyLinearVelocity`) |
| `jump` | Jump apex, airtime, cooldown | implemented in the example script (tracks `Humanoid` state + height) |
| `auto-catch` | Automation detection over scripted catch windows | **rejected** by the example until your catch system exposes hooks |
| `dime-accuracy` | Precision placement scoring | **rejected** by the example until your drill scoring is exposed |

Unimplemented tests answer `rejected` with a message naming the missing integration. That is
deliberate: a wrong or invented number is worse than an honest refusal.

## 6. Anti-cheat channel

`GET /v1/anticheat/status` must be fed by detections your game actually raised. The overlay shows
them under **Server detections** and never fabricates an entry. The client-side checklist on the
same screen is computed locally from the real link (signature acceptance, developer account,
private-session flag, token expiry, clock skew) and is labelled as client-side.

## 7. Safety boundaries

This tool is built for **your** game only:

* no code injection, no DLL loading, no memory reads inside Roblox;
* no input synthesis into another process;
* no attempt to bypass Roblox security, rate limits or anti-cheat;
* all actions terminate at your own service and your own private test session.
