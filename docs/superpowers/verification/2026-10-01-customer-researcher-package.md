# Customer Researcher package verification

Date: 2026-10-01 (Europe/Prague). Status: **Implemented; acceptance evidence green.**

Implements `docs/superpowers/specs/2026-10-01-customer-researcher-package-design.md` with its
amendment. The package `src/Applications/IntoChat/Apps/CustomerResearcher/` is now spec + tests +
two behaviors over platform contracts only; the legacy `app.cs` (which fronted the compiled
module through `ICustomerResearcher`) is deleted. The compiled module, IntoChat.csproj, AppHost
and the E2E suite are untouched, per the spec's out-of-scope list.

## What was built

- `behaviors/surface.cs` — composes the window on "open" (company field, research/stop buttons,
  status text, `IWebBrowser.Configure` pairing browser → driver → status, layouts, surface,
  `EnsureOpenAsync`). Idempotent grain calls; answers the window JSON.
- `behaviors/research.cs` — the `CompanyResearchAgent` loop ported faithfully (same prompts,
  bounded 3-step navigation, Bing wrapper decoding, refresh snapshot, verbatim-evidence
  validation and refusal messages), over `IPlaywright`/`IScriptedBrowser`, `ILLM`
  (`IScriptedLLM` via the `Model` setting, `IGemma4` default), and `IPostgresTable`
  (`Define` + idempotent `Upsert` keyed by the query hash). Handles the app operations
  `research`, `stop`, `result` and the two button streams; all table access lives in this file
  (per-behavior table ownership).
- `tests.cs` — six deterministic scenarios (`dbtest:` protocol): surface composition, idle stop,
  verified research saved and read back via the `result` operation, unverifiable refusal saving
  nothing, and a race-honest stop-during-research scenario. Settings route the behavior to
  `scripted/<scope>/browser` and `scripted/<scope>/model`; the gate never reads the table
  directly (gate-visibility rule).
- `ShippedAppFacts.TheResearcherPackageUsesOnlyPlatformContracts` — asserts no legacy `app.cs`,
  exactly the two behaviors, no reference to `CustomerResearcher.Contracts` anywhere, and every
  `#:project` directive within the platform allowlist (Apps, Flutter, Playwright, AI, Postgres).

## Criterion 7 report

- **Stop idiom implemented:** an in-process `CancellationTokenSource` per research, cancelled by
  the stop operation/button; a new research supersedes the previous via `CancelAsync`. Because one
  behavior file is one process, no persisted generation row is needed: a process death kills the
  in-flight research with it, and buffered research/stop *operations* replay in publication order
  on the single `AppInvoked` stream. Button clicks are two streams; a research click buffered
  before a stop click may replay after a wake and run again — the amendment's accepted residual
  semantics (user stops it again; saves are idempotent). Stop granularity: immediate in a live
  run; next-run-boundary across restarts.
- **Missing/awkward contracts:** none blocking. Noted: (a) model selection from a string setting
  supports `scripted/<key>` plus one compiled default (`IGemma4`) — a general script-side
  `ModelAddress` would be nicer; (b) per-behavior table ownership forces research/stop/result
  into one file (already on the spec's open fronts); (c) the previous attempt's ordering
  reproduction fact is kept in `CSharpSubscriptionFacts` as a platform regression.
- **Final `#:project` lines:** research.cs → Apps, Flutter, Playwright, AI, Postgres Contracts;
  surface.cs → Apps, Flutter Contracts; tests.cs → Apps, Flutter, Playwright, AI Contracts.
  No line names `DigitalBrain.Modules.CustomerResearcher.Contracts` (criterion 1's static half,
  enforced by the new fact).

## Verification results

- All three scripts compiled standalone against the real contracts projects plus
  `DigitalBrain.Client` (scratch csproj per script, 0 errors each) — semantic check beyond the
  parse-only shipped-app fact.
- `dotnet test src/IntoChat/tests/IntoChat.Tests.Unit` — 14/14 passed (includes the new
  platform-contracts fact and the shipped-app parse facts over the new files).
- `dotnet test src/Modules/DigitalBrain/Apps/tests/DigitalBrain.Modules.Apps.Tests.Unit` — 59/59.
- `dotnet test src/Modules/DigitalBrain/CustomerResearcher/...Tests.Unit` — 13/13 (1 live-gated
  skip). The module still builds and passes beside the package (coexistence, criterion 5).
- `aspire run` smoke: all resources reached Running (sandbox started on demand);
  `ShippedAppPublisher` committed the changed package and ran `IAppVerification` through the
  real sandbox. Behavioral acceptance evidence (criterion 1's dynamic half):
  - the live Postgres `customer-research` database afterwards contained three `dbt_*` tables,
    one holding `Acme Robotics | info@acme.example | Prague, Czechia` — written by
    `behaviors/research.cs` through `IPostgresTable` during the verification run's research
    scenario (the two empty tables match the refusal and stop scenarios);
  - the IntoChat console carried **no** `stays unpublished` warning and **no**
    `Shipping ... failed` error for any package (the host logs at Warning minimum, so green
    publishes are silent by design; the failure paths log at Warning/Error and did not fire).
- The desktop session ended before a signed-in window walk-through; surface composition is
  covered by the gate's "open" scenario against the real runtime.

## Remaining follow-ups (the cleanup change)

Delete `DigitalBrain.Modules.CustomerResearcher` (module, contracts, endpoints, store), drop its
references from IntoChat.csproj and AppHost, retarget its unit-test coverage that still matters
(agent-loop edge cases) onto the package gate or platform suites, and relocate the platform E2E
suite per the IntoChat-cleanup plan. App-level (vs behavior-level) table scoping remains an open
front.
