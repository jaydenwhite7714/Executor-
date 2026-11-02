# Driven Test Engine — Testing Service Protocol v1

The desktop overlay never talks to Roblox. It talks to **your** server-side testing system, which
is the only component allowed to run a test inside your game. This document is the contract both
sides implement.

- Client: `DrivenTestEngine.Core` (`HttpTestServerTransport`, `RequestSigner`).
- Reference server: `server/` (Node.js, authentication + streaming implemented, test execution
  deliberately not implemented).

---

## 1. Transport

| Property | Value |
| --- | --- |
| Scheme | `https` (plain `http` is accepted by the client **only** for `localhost`/`127.0.0.1`) |
| Base URL | origin or origin + path prefix, e.g. `https://dte.example.com/` |
| Encoding | JSON, UTF-8, `camelCase` property names |
| Auth | HMAC-SHA256 request signature, plus a bearer session token after the handshake |
| Protocol header | `X-DTE-Protocol: 1.0` |

Base URL may include a path prefix (`https://example.com/dte/`). All routes below are appended to it.

---

## 2. Request signing

Every request — including the health probe — carries these headers:

| Header | Meaning |
| --- | --- |
| `X-DTE-Key-Id` | Identifier of the signing key issued to the developer account |
| `X-DTE-Timestamp` | Unix time in **seconds** (UTC) |
| `X-DTE-Nonce` | 128-bit random value, lower-case hex, never reused |
| `X-DTE-Signature` | Lower-case hex HMAC-SHA256 of the canonical string |
| `X-DTE-Protocol` | `1.0` |
| `X-DTE-Client` | Client version, informational |

Canonical string (`\n` = single LF, no trailing newline):

```
METHOD                       ← upper-case, e.g. POST
PATH_AND_QUERY               ← exactly what is sent, e.g. /v1/logs?limit=100&since=17
TIMESTAMP                    ← same string as the header
NONCE                        ← same string as the header
SHA256_HEX_LOWERCASE(BODY)   ← hex digest of the raw body bytes; empty body = SHA256("")
```

Signature = `hex(HMAC_SHA256(key = signing secret, message = canonical string))`.

Server obligations:

1. Recompute the signature and compare in constant time (`timingSafeEqual`).
2. Reject when `|now - timestamp| > 300 s` → `clock_skew`.
3. Reject a nonce that was already used inside the 300 s window → `replay_detected`.
4. Never log the signing secret.

### Worked example (real, reproducible vector)

Plain (unencoded) values — the body is the exact byte sequence that is signed:

```
secret      = dev-secret-change-me-0123456789abcdef
method      = POST
path+query  = /v1/session/handshake
timestamp   = 1767225600
nonce       = 9f2c4e6a8b0d1f3579acebdf02468ace
body        = {"clientId":"abc123","clientVersion":"1.0.0","platform":"windows","protocol":"1.0","requestedScope":"tests:run"}
```

```
SHA256(body)  = 8c635d327b2cb1ef4b84bf710c2a4f5c94302df80f464af608f7bf54b278c285

canonical     = POST\n/v1/session/handshake\n1767225600\n9f2c4e6a8b0d1f3579acebdf02468ace\n8c635d327b2cb1ef4b84bf710c2a4f5c94302df80f464af608f7bf54b278c285

signature     = 79dea419466d228d93d074b8f01eed200fea7391bb9abd078bf9febb3b0c87ab
```

The same vector lives in `server/test-vector.json` and is asserted by `npm test` in `server/`, so
the documented algorithm and the reference implementation cannot drift apart. The C# client
(`RequestSigner`) implements exactly this construction.

---

## 3. Error envelope

Every non-2xx response:

```json
{
  "error": {
    "code": "forbidden",
    "message": "Human-readable reason shown verbatim in the overlay.",
    "hint": "Optional next step.",
    "details": { "field": "value" }
  }
}
```

| `code` | Typical HTTP | Meaning |
| --- | --- | --- |
| `unauthorized` | 401 | Missing/invalid key id, signature or session token |
| `clock_skew` | 401 | Timestamp outside the 300 s window |
| `replay_detected` | 401 | Nonce already used |
| `forbidden` | 403 | Valid signature, but the caller may not do this |
| `developer_only` | 403 | Account is not on the developer allow-list |
| `private_session_required` | 403 | Not a private, developer-owned test session |
| `validation_failed` | 400/422 | Malformed body or parameters |
| `unknown_test` | 422 | Test id not enabled for this session |
| `rate_limited` | 429 | Back off |
| `not_implemented` | 501 | Route exists but the server has no implementation yet |
| `internal_error` | 5xx | Server fault |

---

## 4. Endpoints

### `GET /v1/health`
Signed, no session token. Answers before any session exists.

```json
{
  "status": "ok",
  "serverVersion": "1.0.0",
  "protocolVersion": "1.0",
  "game": "Driven Football",
  "time": "2026-11-02T12:00:00.000Z"
}
```

`time` is used by the client to detect clock skew before signing itself into a 401 loop.

### `POST /v1/session/handshake`
Signed, no session token.

Request:

```json
{
  "clientId": "stable random id generated on first launch",
  "clientVersion": "1.0.0",
  "protocol": "1.0",
  "platform": "windows",
  "requestedScope": "tests:run logs:read anticheat:read anticheat:write"
}
```

Response (200):

```json
{
  "sessionToken": "opaque-bearer-token",
  "expiresAt": "2026-11-02T12:15:00.000Z",
  "serverVersion": "1.0.0",
  "protocolVersion": "1.0",
  "account": { "robloxUserId": "1234567", "displayName": "YourName", "isDeveloper": true },
  "testSession": {
    "sessionId": "private-test-session",
    "placeId": "9876543210",
    "universeId": "987654321",
    "serverInstanceId": "job-id",
    "isPrivateTestSession": true
  },
  "capabilities": ["tests:run", "logs:read", "anticheat:read", "anticheat:write"],
  "allowedTests": ["speed", "jump", "auto-catch", "dime-accuracy"]
}
```

The client refuses to enter the connected state unless **all** of the following hold, and the
server must enforce the same rules independently:

- a non-empty `sessionToken` that is not already expired;
- `account.isDeveloper === true`;
- `testSession.isPrivateTestSession === true`;
- when the operator configured an expected Roblox user id, `account.robloxUserId` matches it.

`allowedTests: []` means "no tests advertised"; unknown ids are hidden rather than guessed.

### `POST /v1/session/refresh`
Signed + `Authorization: Bearer <token>`, empty body `{}`. Returns the same shape as the
handshake, with a possibly rotated token. The client refreshes when the token has < 2 minutes left.

### `POST /v1/session/close`
Signed + bearer, body `{}`. Response `200 {"ok": true}`.

### `POST /v1/tests/run`
Signed + bearer.

```json
{ "testId": "speed", "parameters": { "durationSeconds": 4 }, "clientRunId": "32-hex-chars" }
```

Response (202 or 200):

```json
{ "runId": "server-side run id", "state": "queued" }
```

The client then polls the run. A run may only be created when the session is a developer account
inside a private test session — the same gate as the handshake.

### `GET /v1/tests/runs/{runId}`
Signed + bearer. This is the **only** source of a result.

```json
{
  "runId": "server-side run id",
  "testId": "speed",
  "state": "passed",
  "metric": 34.72,
  "unit": "studs/s",
  "threshold": 32,
  "message": "Peak sprint velocity within tolerance.",
  "startedAt": "2026-11-02T12:01:00.000Z",
  "completedAt": "2026-11-02T12:01:06.000Z",
  "serverInstanceId": "job-id",
  "checks": [
    { "name": "developer_account", "passed": true, "detail": "1234567" },
    { "name": "private_session", "passed": true, "detail": "private-test-session" },
    { "name": "velocity_curve", "passed": true, "detail": "sampled 120 frames" }
  ],
  "evidence": { "peakSampleIndex": "118" }
}
```

`state` ∈ `queued | running | passed | failed | errored | rejected | cancelled | expired`.
Anything else is mapped to `unknown` by the client (forward-compatible).

### `GET /v1/logs?limit=100&since=<cursor>`
Signed + bearer. Returns entries the overlay shows in **Live Detection Logs**, flagged `SERVER`.

```json
{
  "entries": [
    {
      "id": "17",
      "timestamp": "2026-11-02T12:00:03.000Z",
      "level": "warning",
      "category": "anticheat",
      "message": "Rejected catch input outside human bounds",
      "detail": "latency 12 ms",
      "data": { "userId": "1234567" }
    }
  ],
  "nextCursor": "18"
}
```

`level` ∈ `trace | info | warning | error | critical`; `category` ∈ `client | session | test | anticheat | server`.

### `GET /v1/anticheat/status`
Signed + bearer.

```json
{
  "monitoringEnabled": true,
  "lastScanAt": "2026-11-02T12:00:00.000Z",
  "signals": [
    {
      "id": "signal-1",
      "category": "automation",
      "severity": "high",
      "description": "Catch input arrived faster than humanly possible",
      "subject": "1234567",
      "detectedAt": "2026-11-02T12:00:01.000Z",
      "acknowledged": false
    }
  ]
}
```

`severity` ∈ `info | low | medium | high | critical`.

### `POST /v1/anticheat/monitor`
Signed + bearer, body `{ "enabled": true }`. Arms/disarms monitoring inside the game server and
returns the same shape as `GET /v1/anticheat/status`.

---

## 5. Client behaviour you can rely on

- Polling cadence: run status every 750 ms (120 s budget); logs + anti-cheat every 3 s.
- Session refresh when the token has less than 2 minutes remaining.
- Every failure is displayed: the overlay never substitutes a placeholder value for a missing one.
- `401`/`403` during polling moves the overlay to the `Faulted` state and stops background work.
- All persistence is local (`%LOCALAPPDATA%\DrivenTestEngine`); the signing secret is stored with
  Windows DPAPI, never in clear text.
