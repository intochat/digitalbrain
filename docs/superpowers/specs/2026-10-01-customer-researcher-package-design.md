# Customer Researcher as a proper package

Date: 2026-10-01. Status: ratified for implementation.

## Intent

CustomerResearcher must become a **package** — `app.spec.md` + `tests.cs` + `behaviors/*.cs`
in `src/Applications/IntoChat/Apps/CustomerResearcher/` — whose behaviors act only through
platform neuron contracts over the script edge. Mechanically the same artifact a user builds
by talking to the brain. The compiled module `DigitalBrain.Modules.CustomerResearcher`
(neuron, agent, store, endpoints, surface) is the thing being replaced; it is **not deleted
in this change** — module and package briefly coexist, and the cleanup (delete module, prune
IntoChat.csproj and AppHost, relocate tests) is a separate follow-up once the package is proven.

The platform prerequisites landed in PR #114 (merged to master):

- `IPostgresTable` (Postgres module Contracts): `Define`/`Upsert`/`Delete`/`Read`/`Page`,
  owner-bound at first `Define` to the kernel-stamped brain **and behavior file** (`AppId`),
  physical name hashed from scope + grain key, values parameterized as JSON, signals
  `TableDefined`/`RowUpserted`/`RowDeleted`.
- Self-managing browser sessions: `IWebBrowser.Configure(playwrightKey, statusTextName)`
  pairs a shell browser window with its `IPlaywright` driver and status text; the
  `[PlatformOnly]` connector handles attach/detach; apps consume `IPlaywright.Read()`,
  `BrowserReady`/`BrowserUnavailable`, and `BrowserNotConnectedException`.
- `IScriptedBrowser : IPlaywright`: deterministic observation replay (`Script`,
  `RequestedUrls`), persisted in grain state — the gate's browser, mirroring `IScriptedLLM`.

## Package structure

Two behaviors, split by the two constraints that govern it:

- **`behaviors/surface.cs`** — composes the UI on the app's `open` operation: company
  `ITextField`, research/stop `IButton`s, status `IText`, `IWebBrowser.Configure` pairing
  browser → driver → status, `ILayout`s, `ISurface`, `IWorkspace.EnsureOpenAsync`. Pure
  idempotent grain calls, no data access. Ports `CustomerResearcherSurface.Compose`.
- **`behaviors/research.cs`** — the research loop, Stop, and all `IPostgresTable` access.
  Everything touching the table lives in this one file because table ownership is stamped
  per behavior file (PR #114 README); a separate `stop.cs` could not touch the research
  table or a shared generation row. This fusion pressure is recorded evidence that table
  scoping should later become app-level (open front), at which point stop may split out.

`app.spec.md` keeps the existing scenario headings (open composes the surface; stopping idle
is safe; live research), reworded to the new mechanics, plus a **Stop during research**
scenario. `app.json` and prompts follow the existing shipped-app conventions (WordCount and
Settings are the clean references).

## The research behavior

Port `CompanyResearchAgent` **faithfully**: the same two prompts (bounded link choice
returning `{"linkId":N}`; verbatim-extraction returning the company JSON), the same bounded
3-step navigation loop, search-engine wrapper decoding, candidate-link filtering, the
post-loop refresh snapshot, and the verbatim-substring evidence validation (`Validate`),
re-expressed over contracts: `IPlaywright.Navigate`/`Snapshot`, `ILLM.Generate(InferenceRequest)`
(model selection via the app's settings/account slots as other shipped apps do), progress to
the status `IText`. Results go to an `IPostgresTable`: idempotent `Define` (research columns
`company_name`, `website`, `location`, `email`, `phone`, `industry`, `summary` as text,
`evidence` jsonb, `updated_at` timestamptz), `Upsert` keyed by a hash of the query, so
at-least-once redelivery dedups through the upsert.

Setup before the subscription loops must be cheap and idempotent; run-to-run state lives in
neurons; derived presentation is re-derived wholesale, never patched.

## Stop

A script cannot hold a grain-side `CancellationTokenSource`. The idiom: `research.cs` runs
**two concurrent subscription loops** in one process —

- the research loop: on research-`ButtonClicked` (or the app's `research` operation), run the
  agent loop under a local `CancellationTokenSource`;
- the stop loop: on stop-`ButtonClicked`, cancel that local CTS (immediate, in-process) and
  bump a persisted **generation row** in the same `IPostgresTable`.

The generation row covers the cross-run case: a reaped script woken by a buffered research
signal reads the generation before starting and before saving, and abandons silently when
stale. Stop granularity is honest: immediate within a live run; at the next run boundary
otherwise. The spec documents this. If two concurrent `brain.On` loops in one script do not
behave correctly over the script edge, that is a **platform finding to report, not to work
around** — it would be the last kernel gap before cleanup.

## tests.cs — the deterministic gate

One `dbtest:pass`/`dbtest:fail` line per non-live scenario, exit non-zero on failure, scratch
GUID-scoped installs, never a live model or browser:

- `IScriptedBrowser.Script(...)` with observation sequences (search page → official site →
  contact page) whose page text contains the values the scripted extraction answers verbatim,
  so evidence validation passes.
- `IScriptedLLM.Script(...)` with the link-choice JSON then the extraction JSON.
- Assert: the `IPostgresTable` row after research; status text transitions; the Stop scenario
  cancels and **saves nothing**; stopping idle answers "Stopped." and does not fail.

## Criteria of done

1. **Module-free verification.** The package's `tests.cs` runs green through
   `IAppVerification`/`ITestScriptRunner` on a host composition that does **not** include
   `CustomerResearcherModule`, proven by a fact alongside the existing `ShippedAppFacts`
   pattern. No `#:project` line in any behavior or test references
   `DigitalBrain.Modules.CustomerResearcher.Contracts` — every directive names a platform
   module (Apps, Flutter, Playwright, AI, Postgres).
2. **Faithful behavior.** The research loop preserves `CompanyResearchAgent` semantics:
   bounded steps, prompt texts, verbatim evidence validation, the same refusal messages for
   ambiguous/unverified results, upsert-idempotent saves.
3. **Stop works at both granularities**, covered by the gate: an in-flight scripted research
   is cancelled and saves nothing; a stale generation prevents a woken run from saving.
4. **Surface parity.** Opening the app composes the same surface (toolbar, browser, status)
   and the browser pairing goes through `IWebBrowser.Configure`; re-running composition is
   idempotent and does not reset a live session or status.
5. **Coexistence, no collisions.** The package installs and runs on the current full
   composition next to the still-present module, using its own grain-key space; module and
   package do not fight over neurons or tables. Existing module unit tests stay green.
6. **Suites and smoke.** Green: the Apps module unit suite, the IntoChat unit suite
   (including `ShippedAppFacts`), the Postgres and CustomerResearcher module suites
   (untouched or adjusted), and `aspire run` from `src/IntoChat/IntoChat.AppHost` with
   all resources Healthy; the researcher window opens and composes against the running app.
7. **Report delivered**: the Stop idiom as implemented and its observed granularity; any
   missing or awkward contract encountered (named precisely); the final list of `#:project`
   lines per behavior; any platform findings (especially concurrent `brain.On` loops).

## Amendment (2026-10-01): ordering and gate visibility

The first implementation attempt stopped on a verified finding (reproduction:
`CSharpSubscriptionFacts.OppositePublicationOrdersAcrossSubscriptionsHaveIdenticalWakeReplayPayloads`):
signals carry no sequence, and separate `brain.On` subscriptions buffer independently, so after a
wake a research request buffered before Stop is indistinguishable from one published after it.
This is resolved at the **package design level**, not with a kernel change:

- **Single ordered intent stream.** Research and Stop must arrive through **one source neuron**
  so the platform's within-subscription ordering applies: both as operations on the app's own
  `IApp` (`AppInvoked` carries `"research"` with the query and `"stop"`) — the ordering problem
  then cannot arise. Because one behavior file is one process, an in-process
  `CancellationTokenSource` is the whole cancellation mechanism: a research that was live when
  its process died is simply gone, and its buffered request replays in order with the Stop that
  follows it. No persisted generation row is required; the implementation dropped it.
- **Accepted residual semantics:** if any intent still arrives out of band, a research request
  that replays after a wake simply runs again and the user may stop it again; saves stay
  idempotent. Criterion 3 is to be read with this amendment: the gate covers (a) in-flight
  cancellation saves nothing, and (b) a run that captured generation G does not save after Stop
  bumped it — not cross-buffer causal ordering, which the single-stream design makes moot.
- **Gate visibility:** `tests.cs` must not need ownership of the behavior's table. Assert results
  via the `RowUpserted`/`TableDefined` signals, via an app operation that answers the stored
  result, or via the read-only `IPostgres.Query` door using the physical name from `TableDefined`.
  Do not add ownership bypasses to `IPostgresTable`.

The reproduction test is kept as a platform regression fact documenting the ordering property.
A kernel-level causal stamp on signals remains a possible future front; it is not required here.

## Out of scope

Deleting the module; changing IntoChat.csproj, AppHost, or the E2E suite; app-level table
scoping; cross-app subscription scopes; renderable palette; run tokens. These follow in the
cleanup change once criterion 1 holds.
