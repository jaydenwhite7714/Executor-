# Driven Test Engine (DTE)

A compact, always-on-top Windows overlay for running **server-authoritative** tests inside your own
Roblox game, Driven Football. Dark charcoal surfaces, blue accent, smooth motion, snap-to-edge,
resizable, and a hide tab that keeps a sliver of the app on the right edge of the screen.

```
┌──────────────────────────────────────────────┐
│ ⚡ Driven Test Engine          [LIVE] ‹  ✕   │
├──────────────────────────────────────────────┤
│  Tests   Monitor   Logs   History   Link     │
├──────────────────────────────────────────────┤
│  SERVER-RUN TESTS        4 of 4 available    │
│  ┌────────────────────────────────────────┐  │
│  │ ⚡ Speed Test                     PASS  │  │
│  │ 34.72 studs/s · Server checks 3/3      │  │
│  │ Sample window ●──────────  4 s         │  │
│  │                          [ Run test ]  │  │
│  └────────────────────────────────────────┘  │
├──────────────────────────────────────────────┤
│ ● Connected · place 9876543210     v1.0.0    │
└──────────────────────────────────────────────┘
```

## What this is — and what it is not

**Is:** a client for *your* testing service. It signs every request (HMAC-SHA256), opens a session
only for a developer account inside a private test session, asks your game server to run a test,
and displays exactly what your server measured.

**Is not:** an executor, injector, or Roblox bypass. DTE never touches the Roblox process, never
synthesises input, never reads memory and never talks to Roblox directly. There is no code path in
this repository that does any of that, by design. See
[`docs/ROBLOX-INTEGRATION.md`](docs/ROBLOX-INTEGRATION.md).

## Features

| | |
| --- | --- |
| **Speed Test** | Server samples the avatar's velocity curve; peak + average reported back |
| **Jump Test** | Apex height, airtime and cooldown across N attempts |
| **Auto Catch Test** | Scripted catch windows scored by your automation detection |
| **Dime Accuracy Test** | Precision placement scoring in your drill |
| **Anti-Cheat Monitor** | Server detections plus a real, local session-integrity checklist |
| **Live Detection Logs** | Client + server entries, level filter, search, copy, auto-scroll |
| **Test History** | Local archive with pass rate, filters, CSV export |

Overlay: right-edge dock, magnetic snapping, drag by title bar, resize from any edge, hide/show tab,
always-on-top toggle, reduce-motion switch, geometry remembered between launches.

## Requirements

- Windows 10 1809+ or Windows 11
- [.NET 8 SDK](https://dot.net) to build, .NET 8 Desktop Runtime to run the framework-dependent build
- [Node.js 18 LTS or newer](https://nodejs.org/) for the bundled local reference service. The publish
  includes its service files and DTE starts it automatically; Node.js itself must be installed.
- A testing service implementing [protocol v1](docs/PROTOCOL.md) for real game-side testing — the
  `server/` reference is authentication-only

## Build

```bat
:: from a normal command prompt in the repository root
build.cmd
```

or with options:

```powershell
.\build.ps1                                  # Release build + framework-dependent publish
.\build.ps1 -SelfContained                   # single-folder build with the runtime included
.\build.ps1 -Configuration Debug -NoPublish  # build only
```

Output: `publish\DrivenTestEngine.exe`.

The same steps run in CI (`.github/workflows/build.yml`, `windows-latest`).

## First run

1. Install Node.js 18 LTS or newer once. On first launch, DTE starts the bundled loopback service.
2. On a clean profile, the overlay seeds `http://127.0.0.1:8791`, `dte-dev-key`, and the documented
   **demo-only** signing secret. It protects the secret with Windows DPAPI and enables auto-connect.
   When the service starts later, the overlay retries every 10 seconds while Auto-connect is on.
3. **Link** shows the health/handshake outcome. For another service, enter its URL and credentials;
   existing preferences are never overwritten by the first-run defaults.
4. The reference server is authentication-only: it returns `501 not_implemented` for test execution.
   It self-asserts demo developer/private-session claims and is not production authentication.

The bundled credential is public and loopback-only; never expose the reference service or reuse that
secret for a deployed service. The Windows secret is protected at rest with current-user DPAPI. See
[`docs/IMPLEMENTATION-PLAN.md`](docs/IMPLEMENTATION-PLAN.md) for the inspected bridge gaps and staged
plan for legitimate game integration.

## Repository map

```
src/DrivenTestEngine.Core   net8.0  protocol, transport, engine, history, settings (no UI deps)
src/DrivenTestEngine.App    net8.0-windows  WPF overlay, MVVM, theme, windows
server/                     Node.js reference testing service (auth + streaming only)
roblox/                     example ServerScriptService bridge for your game
docs/                       PROTOCOL.md, ROBLOX-INTEGRATION.md, ARCHITECTURE.md
```

## Honest status

**Build:** green on `windows-latest` — `dotnet restore`, `dotnet build -c Release` and
`dotnet publish -r win-x64` all pass (`Actions` tab → `build` → latest run).

- The desktop app **compiles clean on Windows**: the `build` workflow restores, builds the solution in
  Release and publishes `win-x64` on a `windows-latest` runner (commit `App: import System.IO` and
  everything after it are green). The first Linux-authored commit had two real errors — a
  `SupportedOSPlatformVersion` that exceeded the target platform version, and a missing
  `using System.IO` (the WPF SDK drops it so `System.IO.Path` cannot clash with `Shapes.Path`) — both
  fixed and re-verified by CI. The build artifact `DrivenTestEngine-win-x64` is attached to the run.
- The app has not been *run* against a live testing service, because that service is yours and does
  not exist yet. Every screen states that plainly instead of showing invented numbers.
- The protocol's authentication half is implemented twice (C# client, Node reference server) and the
  reference server's 30-assertion self-test passes, including the documented signing vector.
- Test **execution** is not included. `POST /v1/tests/run` in the reference server answers
  `501 not_implemented`, and the example Roblox bridge rejects `auto-catch` and `dime-accuracy`
  until those systems are exposed. Nothing is simulated and no sample numbers exist anywhere in the
  app: an unimplemented test shows the server's refusal.
