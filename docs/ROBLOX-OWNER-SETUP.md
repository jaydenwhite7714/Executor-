# Driven Football owner setup: information and Studio steps

DTE's repository does **not** contain the Driven Football game source. The bridge under `roblox/` is
an example only. Do not install it in a live place yet: its endpoint/token are intentionally
unconfigured and its private-session binding is not production-safe.

## What I need from the game owner before implementing the real adapter

Please provide these as text or relevant server-side code snippets (remove API keys, session
cookies, player personal data and production secrets):

1. Roblox **Universe ID** and the exact **Place IDs** used for private testing (numeric IDs from
   Creator Dashboard; these are not player UserIds).
2. The developer/tester **UserId(s)** authorized for the reserved test account. Do not send passwords,
   cookies, Open Cloud API keys or signing secrets in chat.
3. How you create a private test server (reserved server vs. private server), how the service can
   prove its `PrivateServerId`/reserved job, and the intended test avatar/setup flow.
4. Server-side module/function names and safe excerpts for: authoritative sprint/movement rules,
   jump controls/rules, catch resolver/window, dime/pass drill scoring, and anti-cheat detector event
   emission. Include current thresholds and rule versions where applicable.
5. Where the service will run (your PC for local testing or a hosted endpoint) and how it will
   securely hold a least-privilege Open Cloud MessagingService key and result-ingest credentials.

## Safe Studio inspection / installation sequence (after the adapter is ready)

1. In Creator Dashboard, confirm the Universe and test Place IDs; make a **separate private test
   place/server**. Do not use a public live server.
2. Open the test place in Roblox Studio. In Explorer, inspect `ServerScriptService` and identify
   the game-owned modules for the systems above. Do not paste the example bridge into production.
3. Provide the module interfaces/source excerpts to the developer implementing the adapter. Keep
   all Open Cloud keys and result secrets outside Studio scripts; a Roblox server script must never
   contain a credential that grants account/universe-wide API access.
4. Once an adapter is reviewed, install only the reviewed server-only Script into the isolated test
   place's `ServerScriptService`; enable `HttpService` only for that test environment. Configure
   test-only service credentials outside the source. Confirm clients cannot require or read it.
5. Run the test with an authorized developer account in the reserved private job. Check server logs,
   service audit trail, and overlay output agree on the same run ID/job. Verify public-server requests
   and a different UserId are refused before any measurement begins.

## Existing game bridge status

The current sample has hard-coded example `ALLOWED_USER_ID`, a fake example endpoint and a
replace-me bridge token. Its MessagingService command intake has no cryptographic envelope, and its
result endpoint has no matching implementation in `server/`. The current service's demo identity is
not a Roblox verification. Do not deploy this script. The existing speed sample reports velocity
but has no meaningful acceptance threshold; the jump sample measures landing position rather than
apex. Auto Catch and Dime Accuracy are explicit rejections because their real game systems are not
available in this repository. There is no actual detector integration.

Until the owner supplies the game-side interfaces and a private test environment is available, no
one can honestly implement or verify these four real tests or anti-cheat results from this checkout.
