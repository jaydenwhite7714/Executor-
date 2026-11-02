# DTE completion plan — Driven Football only

This is the staged implementation plan based on inspection of the current client, reference service,
and `roblox/DrivenTestEngineBridge.server.lua`. It is intentionally not a claim that the planned
items already work. Do not enable a stage in a live game until its acceptance checks pass.

## Diagnosis: why the local connection appears to fail

The C# HMAC implementation and Node reference server agree on the documented canonical request;
the reference server's self-test validates its signing vector and its own signed handshake. The
handshake route accepts the request body emitted by the client and returns the fields the client
expects. No protocol mismatch was found in those code paths.

The actual first-launch defect was configuration: `AppSettings` initialized the server URL, key id,
secret and `AutoConnect` as empty/false. `OverlayHost.Start()` only attempted a connection when the
saved `AutoConnect` flag was already true. Thus a running service on port 8791 was ignored on a fresh
install unless the user entered all credentials and enabled automatic connection themselves. A
second usability defect: auto-connect was a single attempt, so starting the app before the service
left it offline indefinitely. This change seeds the documented **loopback-only reference profile**
on a clean install (secret protected with Windows DPAPI) and retries at 10-second intervals while
AutoConnect is enabled. Existing non-empty user configuration is not overwritten. The bundled
reference credential is public/demo-only and must never be used outside loopback.

If this fix does not resolve a particular installation, use Link → Test Service and inspect the
specific reported failure. `401 Signature mismatch` means secret/config mismatch; `clock_skew` means
system clock; connection refused means listener/port or Windows Firewall; `403` means the returned
account/private-session claims failed server policy; `Protocol` means invalid response JSON. The
reference service currently self-asserts the developer/private flags and is for local protocol
verification only, not production identity proof.

## Current implementation findings

- Desktop: signed health and handshake, DPAPI credential persistence, local developer/private
  authorization checks, run request/poll UI, server log/anti-cheat polling. It never connects to the
  Roblox process.
- Reference service: local HMAC/replay/session behavior is implemented and tested. It intentionally
  returns `501 not_implemented` for test runs. Its anti-cheat status is empty; its monitor toggle is
  only a state flag, not a detector.
- Roblox bridge: example script listens to MessagingService and posts results to a placeholder URL.
  It has real server-side velocity sampling and jump-state sampling, but is not production-ready:
  placeholder endpoint/token, no verified private-server/job binding, no authenticated command
  envelope or server-side run authorization, and its result route is not implemented by `server/`.
  `speed` currently uses a zero threshold (so any nonzero measurement passes); jump samples height
  at landing rather than recording the actual apex. Treat both as instrumentation examples, not
  accepted final measurements. Auto Catch and Dime Accuracy explicitly reject because game mechanics
  are not exposed. No real anti-cheat detector/event feed exists.
- Integration cannot be completed generically without the Driven Football server's real catch,
  pass/drill scoring, movement rules, reserved test-session identity and detector event APIs.

## Phased plan and acceptance gates

### 0. Local startup / handshake (this patch)

1. Clean install receives loopback URL/key/sample secret and auto-connect enabled; secret is stored
   only through current-user DPAPI. Existing configured profiles remain untouched.
2. Retry local connect after service startup while enabled; stop once connected or preference disabled.
3. Verify with Windows CI build/publish and reference service self-test. Manually confirm Windows
   Link shows verified session against local Node reference server. This is not Roblox verification.

### 1. Service identity and secure private-session authorization

1. Replace reference server's self-asserted `isDeveloper`/`isPrivateSession` with deployment
   configuration backed by a developer UserId allow-list and an authoritative private test-session
   registry (reserved job/session ID and place/universe ownership).
2. Enforce authentication and scopes on every run, result, log and anti-cheat route; bind token to
   key ID, client ID, UserId, private server/job ID and expiry. Reject public/unknown sessions.
3. Add bounded JSON/body validation, durable nonce protection, per-user/key rate limits, audit trail,
   secret rotation, TLS for non-loopback endpoints, and prohibit shipping demo secrets.
4. Acceptance: adversarial tests prove public server, wrong user/place, expired/replayed token,
   unsupported test, bad signature and over-limit requests are rejected; no test can reach another
   user/session.

### 2. Authenticated run dispatch and result channel

1. Implement a run state machine on service: validate+persist queued run, publish only to the
   configured universe/topic using a server-held least-privilege Open Cloud key, and expose poll
   responses. Add authenticated Roblox result ingestion with idempotent run IDs and server/job binding.
2. Replace bridge placeholder endpoint/token. Authenticate each result with per-session credentials
   or a signature, enforce freshness/replay, retry safely, and persist terminal states. Never accept
   client-originated measurements.
3. Bridge must verify command ID, allowed test, exact reserved private job/user, bounded parameters,
   expiry and rate limit before execution. Use durable deduplication where feasible and return
   rejected/expired rather than fabricate outcomes.
4. Acceptance: end-to-end local integration contract tests cover queued→running→terminal result,
   duplicate delivery, timeout, worker/server disappearance and invalid callback. No “passed” unless
   authoritative game-side checks pass.

### 3. Speed and jump tests (after game-owner integration)

1. Replace sample handlers with adapter calls into Driven Football's own movement/jump authority.
   Measure from server-owned state; identify coordinate units, physics ownership, sampling cadence,
   respawn/teleport handling and expected legitimate limits with the game developer.
2. Define explicit test protocol: avatar setup, requested duration/attempt count, acceptance thresholds,
   measurement confidence, cancellation and result evidence. Store threshold/version in service, not
   a hard-coded zero. For jump record peak during ascent, landed airtime and cooldown separately.
3. Bind all samples to the developer's reserved test avatar and current private job. Do not force
   arbitrary player state; test should observe a controlled, game-supported drill.
4. Acceptance: reproducible tests in a private reserved server; known legitimate control values,
   invalid/missing character and interruptions produce honest rejected/errored results. Results
   include provenance and actual threshold.

### 4. Auto Catch and Dime Accuracy

1. Add narrowly scoped server-side instrumentation hooks to the actual catch resolver and passing /
   dime drill scorer. Do not infer behavior from desktop inputs or modify client input.
2. Auto Catch: record authoritative catch opportunities, target/player eligibility, timing windows,
   outcomes and existing game detector signals. Define the game's documented test scenario and score
   from those events; a catch success alone must not imply automation.
3. Dime Accuracy: call the real server scoring/placement system with the private test drill and
   report its canonical accuracy, target, attempt count and rule/version. Do not build a parallel
   scorer that can disagree with production mechanics.
4. Keep both tests disabled/rejected until instrumentation hooks and thresholds are supplied by the
   game owner. Acceptance requires server-only measurements and evidence tied to a private run.

### 5. Real anti-cheat results

1. Integrate existing Driven Football server detections through a typed event adapter, preserve source,
   timestamp, severity, rule ID/version and relevant redacted evidence; stream/paginate from service.
2. Separate “monitor requested” from “detector running”; the service must report actual detector
   health and data freshness. Empty results mean no recorded signals, not proof of clean play.
3. Protect sensitive player data, apply retention/access policy, authenticate game-server ingest, and
   prevent desktop client from writing/forging detections. Keep local connection-integrity checks
   visually distinct from game anti-cheat signals.
4. Acceptance: a controlled test event raised by the real detector appears once with provenance;
   no generated/sample event is allowed in production.

### 6. Beginner-friendly release and operational safeguards

1. Ship a setup wizard/profile for the developer's service URL and key enrollment. Production
   credentials must be provisioned/rotated securely; never compile a shared production secret into
   the app or Roblox script. Keep the first-run built-in credential limited to loopback reference mode.
2. Add explicit service health, connection retry/backoff, actionable errors, setup guide, backup and
   uninstall/data-clear instructions. Auto-connect must never weaken authorization.
3. Release only after Windows build/publish, automated protocol/integration/security tests and manual
   private-session acceptance. Document unsupported tests and data retention. No simulated results.

## Required game-owner inputs before phases 2–5

- Driven Football universe/place IDs; developer UserIds and how reserved private test sessions are
  created/verified; actual server-job binding strategy.
- Existing movement and jump rules/thresholds and any supported drill controls.
- Catch resolver, pass/dime scorer, and anti-cheat event APIs or the relevant server-side modules.
- Deployment target for the service, secure Open Cloud key management, result-ingest networking and
  retention policy.

Until these interfaces are provided and tested, the only accurate status is: loopback handshake is
available; the reference server does not execute tests or provide real anti-cheat detections; the
Roblox bridge is an incomplete integration example; Auto Catch and Dime Accuracy are intentionally
unavailable.
