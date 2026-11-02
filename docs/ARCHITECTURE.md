# Architecture

```
DrivenTestEngine.sln
├── src/DrivenTestEngine.Core      (net8.0, no UI, no third-party packages)
│   ├── Protocol/                  wire constants, JSON options, HMAC signer, enum coercion
│   ├── Models/                    request/response DTOs, session validation, run state
│   ├── Transport/                 ITestServerTransport + HTTPS/HMAC implementation
│   ├── Engine/                    EngineClient: session state machine, polling, result pipeline
│   ├── Logging/                   bounded live log store (+ rolling file)
│   ├── History/                   local run archive (JSON, atomic writes, CSV export)
│   ├── Settings/                  settings file + DPAPI secret handling
│   ├── Security/                  DPAPI wrapper (P/Invoke), credential model
│   ├── Tests/                     catalogue metadata for the four tests
│   ├── Mvvm/                      ObservableObject, RelayCommand, AsyncRelayCommand
│   └── Util/                      AppData paths
├── src/DrivenTestEngine.App       (net8.0-windows, WPF, WinExe)
│   ├── App.xaml(.cs)              startup, single-instance guard, crash containment
│   ├── Themes/                    Palette (colour/icon geometries), Controls, Templates
│   ├── Windows/                   OverlayWindow (shell) + EdgeTabWindow (hide tab)
│   ├── Views/                     Tests, Monitor, Logs, History, Link
│   ├── ViewModels/                Shell + one per page + per-test cards
│   ├── Controls/                  MotionContentControl (animated page host)
│   ├── Infrastructure/            converters, DPAPI→PasswordBox binding, auto-scroll, DWM helpers
│   └── Services/                  AppServices (composition root), OverlayHost (window lifetime)
├── server/                        reference testing service (Node.js, auth + streaming only)
├── roblox/                        example ServerScriptService bridge for your own game
└── docs/                          protocol, Roblox integration, this file
```

## Threading model

* `EngineClient` owns one background loop (`PeriodicTimer`, 3 s) that polls logs, anti-cheat state
  and refreshes the session token when it is within 2 minutes of expiry.
* Test runs are polled by the awaiting command (750 ms) up to 120 s.
* Engine events fire on worker threads; view models marshal them through `UiDispatcher`, which
  posts to the WPF dispatcher. View models are only mutated on the UI thread.
* `AsyncRelayCommand` prevents re-entry and routes faults to the owning tile instead of the
  dispatcher crash handler.

## State machine

```
Disconnected ──Configure──► Disconnected (ready)
      │
      ├──Connect──► Connecting ──health ok──► handshake ──authorised──► Connected
      │                 │                          │
      │                 └──failure──► Faulted ◄────┘ (refused / bad signature / not developer)
      │
      └──Close session──► Disconnecting ──► Disconnected
```

`Connected` requires a validated `ServerSession`: token present and unexpired, `isDeveloper`,
`isPrivateTestSession`, and (optionally) the expected Roblox user id. Local checks mirror, never
replace, the server's own enforcement.

## Data on disk

`%LOCALAPPDATA%\DrivenTestEngine\`

| File | Contents |
| --- | --- |
| `settings.json` | endpoint, key id, overlay geometry, preferences, DPAPI-protected secret blob |
| `history.json` | archived runs exactly as the server reported them (max 500) |
| `logs\dte-YYYYMMDD.log` | append-only tab-separated copy of the live log |

Settings and history are written through a temp file + atomic move. The signing secret is never
stored in clear text; DPAPI ties the blob to the current Windows user.

## Design system

Black/charcoal surfaces (`#0A0C10` → `#161C24`), a single blue accent ramp (`#3B82F6` / `#6DA9FF`),
10 px card radii, 1 px low-contrast borders, and motion limited to 140–210 ms cubic easing
(page fade/slide, switch knob, nav indicator, hover colour ramps). Window chrome is custom
(`WindowChrome`, `CaptionHeight=0`): title-bar drag, edge resize, magnetic snapping, DWM rounded
corners on Windows 11, always-on-top by preference. Every animation is disabled by the
**Reduce motion** setting.
