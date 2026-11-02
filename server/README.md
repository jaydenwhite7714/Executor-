# Reference testing service (protocol v1)

A zero-dependency Node.js server that implements the **authentication and streaming half** of the
Driven Test Engine protocol, so you can prove the link works before your game is wired up.

```bash
cd server
npm test          # 30 assertions: signing vector, replay protection, sessions, error codes
npm start         # listens on http://127.0.0.1:8791
```

## What is implemented

- HMAC-SHA256 request verification (constant-time compare) on every route, including `/v1/health`.
- 300 s clock-skew rejection and nonce replay rejection.
- Session handshake, refresh and close with bearer tokens.
- Developer-account and private-session gates (configurable, so you can watch the client refuse).
- Log streaming with a cursor, and the anti-cheat status/monitor channel.
- The documented known-answer signing vector (`test-vector.json`) is asserted on every test run.

## What is deliberately NOT implemented

`POST /v1/tests/run` answers **501 `not_implemented`**. A test result may only come from your own
Roblox game server, so this process refuses to invent one — the overlay will show that refusal
verbatim. See [`../docs/ROBLOX-INTEGRATION.md`](../docs/ROBLOX-INTEGRATION.md) and
[`../roblox/DrivenTestEngineBridge.server.lua`](../roblox/DrivenTestEngineBridge.server.lua) for the
part you add.

## Try it with the overlay

1. `npm start` in this folder.
2. In the overlay open **Link** and enter:
   - Server URL: `http://127.0.0.1:8791/` (plain http is allowed by the client for loopback only)
   - Key id: `dte-dev-key`
   - Signing secret: `dev-secret-change-me-0123456789abcdef`
3. Press **Connect**. You should see `Session verified`, the developer badge, the place id, and the
   granted capabilities. Run a test: it will be refused with the server's `not_implemented` message,
   which is the honest result.

## Configuration

| Variable | Default | Meaning |
| --- | --- | --- |
| `DTE_PORT` | `8791` | Listen port |
| `DTE_HOST` | `127.0.0.1` | Bind address (`0.0.0.0` to expose on a LAN/tunnel) |
| `DTE_KEY_ID` | `dte-dev-key` | Signing key id accepted from clients |
| `DTE_SECRET` | `dev-secret-change-me-…` | Signing secret (≥ 32 chars) |
| `DTE_ROBLOX_USER_ID` | `0000000000` | Account reported by the handshake |
| `DTE_DISPLAY_NAME` | `Driven Developer` | Display name reported by the handshake |
| `DTE_PLACE_ID` / `DTE_UNIVERSE_ID` | `0000000000` | Place/universe reported to the client |
| `DTE_IS_DEVELOPER` | `true` | Set `false` to watch the client refuse a session |
| `DTE_IS_PRIVATE_SESSION` | `true` | Set `false` to watch the private-session gate work |
| `DTE_ALLOWED_TESTS` | `speed,jump,auto-catch,dime-accuracy` | Tests advertised by the handshake |
| `DTE_SESSION_TTL` | `900` | Token lifetime in seconds |

## Before you expose it

Replace the stub identity values with facts your service verifies (Open Cloud key introspection,
an allow-list keyed to your developer account, the reserved private-session job id), serve it over
TLS, and keep the signing secret out of source control.
