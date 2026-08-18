# DigitalBrain: Aspire Organization, Core Ratification & Testing SDK

**Date:** 2026-08-18 · **Branch:** `finalv2` · **Status:** Approved design, pending implementation plans per phase

## 1. Purpose

Three intertwined goals, delivered as one program:

1. **Organize the Aspire layer** — finish the half-completed `src/Kernel/Aspire` → `src/Aspire` move and make the solution build again.
2. **Ratify the core model** — unify `master`'s mature Kernel machinery with the Core V2 vision (neurons + synapses for routing over a graph; entities as plain stateful grains), resolving the journal-ownership confusion.
3. **Ship a testing SDK** — packable NuGet packages giving DigitalBrain itself *and community module authors* a three-tier testing ladder over Aspire testing + Orleans TestingHost, proven by an MVP vertical slice and test waves across all seven modules.

## 2. Current state (facts this design rests on)

- `finalv2` = `master` + two "delete trash" commits. The solution **does not build**: ~12 `ProjectReference` paths broke in the Aspire move, `aspire.config.json` points at the old AppHost path, `DigitalBrainHostingExtensions` calls a `DigitalBrainNames` class that doesn't exist (the file declares `DigitalBrainResources`), and `OAuthProviderHosting`/`OAuthCallbackPaths`/global usings were dropped. Kernel projects `Abstractions`, `Core`, `Client`, `Sdk` are gutted (0 source files); full source survives on `master`.
- **No test projects exist**, but `Directory.Packages.props` already pins the full intended stack: `Aspire.Hosting.Testing` 13.5.0-preview, `Microsoft.Orleans.TestingHost` 10.2.2, xunit.v3 4.0.0-pre, `Reqnroll.xunit.v3` 3.3.4, Testcontainers 4.13.0; `global.json` mandates the Microsoft.Testing.Platform runner (test projects are `OutputType=Exe`).
- The Aspire fabric: `AddDigitalBrain("brain")` composes Azurite (clustering/reminders/pubsub tables, journal blobs, stream queues) + `AddOrleans`, with a module projection model (`brain.AddModule<TModule>(...)` → `DigitalBrainModuleProjection.Apply<TResource>`). Modules: AI (Ollama/OpenAI), Memory (Qdrant), UI (Flutter window host), Google, Salesforce, Execution, Introspection, Time.
- On `master`, every neuron owns two durable **bounded** journals (Incoming/Outgoing `NeuronFeed`: 512 entries / 512 KB, monotonic sequence, per-synapse-type tallies, `ResetSnapshot` past retention). The kernel streams chat/voice/owner-command responses by watching the session neuron's Outgoing journal. Separate concepts: `ICorpus` (owner/principal-scoped watermarked story facts) and Orleans.Journaling (grain-state persistence infrastructure whose name collides with the domain concept).
- The Core V2 seed (`src/Kernel/DigitalBrain`, 14 sketch files, not in the `.slnx`, doesn't compile) sketches the vision: `Neuron` (journaled, routed), `Entity<T>` (plain `IPersistentState<T>` grain, e.g. `ChartEntity`), `DigitalBrain : Entity<BrainGraph>`, `Agent : Neuron`.
- Pattern donors mined for this design: **TripRadar** (`E:\projects\TripRadar` — composite resource, parameter factories, feature flags doubling as test switches, deleted-but-recovered `AspireTestAppHost` harness, model-only tests) and **ino/IAW/final** (`E:\intochat\Projects` — test-mode topology stamping, model-driven silo discovery, scenario sessions, BDD mock LLM, `Simulation` reusing production silo config, anti-rot meta-tests).

## 3. Decisions (with rationale)

| # | Decision | Rationale |
|---|---|---|
| D1 | Repair and reorganize the Aspire layer together, as phase 0/1 of this program | The broken move *is* an unfinished reorganization; the SDK needs a settled structure |
| D2 | Three-tier testing ladder: model-only → Simulation → e2e | Each tier catches a class of defect at the cheapest possible cost |
| D3 | E2E boots the **production AppHost + test args**; per-module/community suites use a **minimal composed host** | One production topology, zero drift; community modules can't reference `Projects.DigitalBrain_AppHost` |
| D4 | BDD from day one, including the mock-LLM corpus | `.feature` files triple as tests, deterministic LLM, and routing corpus |
| D5 | Testing SDK is **packable** for community module authors | Modules are an ecosystem; testing is part of the module contract |
| D6 | Every module owns a `Tests/` folder with its own test projects | Community-identical experience; per-assembly fixture sharing keeps boots affordable |
| D7 | Test surface is the **`IDigitalBrain` facade** (restored from `master`), not an invented dialect | `WatchJournalAsync` is a durable, sequence-ordered observation primitive strictly stronger than telemetry polling; owner scoping *is* session isolation |
| D8 | Journal refactor = naming + contract + tests, no mechanics changes | The mechanics are sound; the confusion is the three-way name collision |
| D9 | Ratified core model: neurons journal, entities don't (§5) | User's vision, validated against `master`'s machinery |
| D10 | Vector search: **routing-only in the MVP**, Memory-module-owned, lexical fallback | Proves the seam with real code without dragging embeddings into the kernel |
| D11 | UI testing is headless-first (Flutter web + headless Chromium); desktop-window mode is a local/nightly opt-in | CI-safe, fast; "windows just in case" stays available |
| D12 | Seed library retires; its concepts are absorbed into the restored Kernel projects | `master`'s machinery is far more mature than the 14-file sketch |

## 4. Target solution layout

```
src/Aspire/
  DigitalBrain.AppHost/            production AppHost (aspire.config.json points here)
  DigitalBrain.Aspire.Hosting/     hosting integration (packable)
  DigitalBrain.Aspire/             runtime silo/client integration (packable)
  DigitalBrain.ServiceDefaults/    OTel + health (packable)
src/Testing/
  DigitalBrain.Testing/            Tier 1+2 SDK (packable)
  DigitalBrain.Testing.E2E/        Tier 3 SDK (packable)
  DigitalBrain.Testing.Bdd/        Reqnroll steps + corpus tooling (packable)
src/Kernel/
  DigitalBrain.Abstractions/       contracts (restored from master, then ratified)
  DigitalBrain.Core/               neuron/entity runtime (restored, then ratified)
  DigitalBrain.Client/             IDigitalBrain facade (restored, then ratified)
  DigitalBrain.Kernel/             silo + web API
  DigitalBrain.Mcp/                MCP server
  DigitalBrain.Scripting/          console client
src/Modules/<X>/
  {Module, Contracts, Aspire.Hosting, Tests/}      Tests folder added per module
tests/
  DigitalBrain.Aspire.Tests/       Tier 1 model-only + conformance suite
  DigitalBrain.Simulation.Tests/   Tier 2 simulation smoke + semantics pins
  DigitalBrain.E2E.Tests/          kernel-level e2e against the production AppHost
```

Repair actions (phase 0): fix all broken `ProjectReference` paths; repoint `aspire.config.json`; restore the types dropped in the move and the gutted Kernel sources from `master` (as-is — ratification is phase 1); remove the seed project after absorption; `dotnet build DigitalBrain.slnx -warnaserror` green is the exit criterion.

**Name-constant unification:** one public `DigitalBrainNames` class in `DigitalBrain.Abstractions`, referenced by both Aspire packages (hoisted in phase 1 to dissolve the dual-compiled-type hazard). A Tier 1 conformance test pins it.

## 5. Ratified core model

**Neuron** — journaled, durable, routed. Carries synapse traffic; participates in the owner's graph; owns exactly two bounded traffic journals (Incoming/Outgoing) with sequence numbers, tallies, and `ResetSnapshot` semantics as implemented on `master`. `Agent : Neuron` is the AI kind; `SalesforceAgentNeuron : Agent` reaches Salesforce via MCP.

**Entity\<T\>** — a plain stateful grain: SaveAsync/Read over Orleans.Journaling durable state (the solution's only persistence fabric — there is no `IPersistentState` provider and none is added), plus typed members (e.g. `IChartEntity.AddPointAsync`). **No journal, no synapse membrane.** `ChartEntity` (bounded to the last 1000 points) is the canonical example. `master`'s `ICell` folds into Entity: typed `Entity<T>` is first-class and the interpreted-kind cell tier is **retired** in phase 1 (revisited only if a product scenario demands dynamic kinds).

**The brain** — `Entity<BrainGraph>`: the owner-scoped graph of neurons and synapse connections is state; routing (`ISynapseGraph`: Connect/Disconnect/RouteOutcome) operates on it. Capability routing consults the graph and the capability index; when the Memory module is installed, a semantic index backs routing (§8), with lexical fallback otherwise.

**One message concept:** `Synapse` is the message; synapse *connections* are graph edges. The seed's `Signal` type is dropped.

**Journal ownership contract** (documented in code and in `docs/JOURNALS.md`):

- Neurons own traffic journals. Entities own snapshots. Corpus owns history.
- **The session neuron is the owner's journal hub** — owner-level views watch it (and proxy-read subject neurons via `ReadNeuronJournal`).
- **Writes journal, reads don't:** entity mutations are driven by neurons (synapse fires → handling neuron mutates the entity → that neuron's Outgoing journal records the effect). UI and clients read entities directly, unjournaled.
- Naming: the domain keeps the word *journal* (it is baked into `IDigitalBrain`). The Orleans.Journaling infrastructure wrappers are renamed to durable-state language (`AddDigitalBrainJournalStorage` → `AddDigitalBrainDurableState`, `JournalStorageHosting` → `DurableStateHosting`, etc.) to kill the collision.
- Retention constants (512 entries / 512 KB) stay fixed; the previously-untested semantics (resume sequence, reset-snapshot at the retention boundary, tallies, checkpoint/restore, watcher-drop) are pinned by Tier 2 tests.

**Facade:** `IDigitalBrain` (restored from `master`) gains the entity half: `GetEntity<TEntity>(string name = "default")` alongside `Get<TNeuron>()`, completing "neurons and entities" as the client surface. Existing members (`Owner`, `ActivateAsync`, `NeuronReference<TNeuron>.FireAsync` incl. request/response, `ReadJournalAsync`/`WatchJournalAsync`) are unchanged.

## 6. Testing SDK

### 6.1 Packages

**`DigitalBrain.Testing`** (Tiers 1+2; deps: `Aspire.Hosting.Testing`, `Microsoft.Orleans.TestingHost`, xunit.v3 extensibility):

- Tier 1: `BrainModel.BuildAsync<TAppHost>()` builds the app model without starting anything; fluent asserts over resources, `WaitAnnotation`s, and rendered env vars (via `ExecutionConfigurationBuilder`); automatic `ParameterResource` stubbing; conformance-test bases (name constants, module-projection completeness, faithful-boot topology).
- Tier 2: `BrainSimulation` — an in-process Orleans cluster (`InProcessTestClusterBuilder`) that **reuses the production silo configuration extension**, swapping only storage: memory grain storage, memory streams, in-memory reminders, volatile durable-state provider; fixtures supply explicit `ModuleAssemblies` rather than a generic module parameter. Exposes `Brain` (an `IDigitalBrain` connected in-process), `Grains` (escape hatch), `UniqueId(prefix)`; `sim.Time`, `Capture`, and `MockEmbeddingGenerator` arrive with their phase-3 consumers. Abstract collection-definition bases; each test assembly declares a one-line subclass (xunit.v3 requires `[CollectionDefinition]` in the test assembly).

**`DigitalBrain.Testing.E2E`** (Tier 3; deps: `DigitalBrain.Testing`, `Microsoft.Playwright`):

- `BrainAppHostFixture<TAppHost>` — boots the production AppHost via `DistributedApplicationTestingBuilder` with: test args (`--Parameters:*` stubs for every declared parameter — silos otherwise hang on dashboard prompts — plus `AppHost:*` feature flags to disable heavy surfaces), session container lifetime, volume/mount stripping, proxied-port randomization, silo discovery **from the app model** (`Resources.OfType<ProjectResource>()`) with parallel `WaitForResourceHealthyAsync`, a resource-log collector (ring buffer per resource, dumped to an artifacts path on failure), and start-failure diagnostics (state + health reports + last 40 log lines per resource).
- `BrainTestHost.Compose(...)` — the community path: builds a minimal ad-hoc AppHost (brain fabric + kernel silo + the module under test) without referencing the production AppHost project. Community authors scaffold a test-silo project from the `dotnet new digitalbrain-module` template (phase 5).
- `BrainSession` — a thin decorator over `IDigitalBrain`: `OpenSession()` connects a fresh unique `OwnerId` over the shared fixture (a session is **an owner, not infrastructure** — this is the core performance mechanism). Adds test affordances only: journal-watch waits with timeouts whose failures report `Saw: …` listing observed entries; edge clients `session.Kernel` / `session.Mcp` (`HttpClient` via `app.CreateHttpClient`) so BDD chat scenarios drive the real HTTP/MCP edge rather than bypassing it; cleanup on dispose. Tier 3 facade connectivity reuses the `DigitalBrainScriptHost.ConnectAsync` path (Orleans client from `ConnectionStrings:clustering`).
- Headless UI: lazy Playwright (non-UI tests pay nothing), headless Chromium by default, headed via local opt-in env var, desktop-window mode behind `DigitalBrain:Ui:Mode=Window` (skipped on CI), `WaitUntilState.Load` only (OTLP exporters keep requests in flight forever, so `NetworkIdle` deadlocks), browsers installed by an MSBuild target, canvas-aware assertions (wire-level frames + semantics tree primary, screenshots as evidence).
- Fake external server toolkit: an in-process minimal-API base class with captured-request channels; concrete fakes for Google OAuth (authorization + token endpoints), Gmail, Salesforce, and a stub MCP server. Fixtures inject fake base URLs via env projection onto module resources.
- A `TestOtlpCollector` (span-level assertions across processes) is explicitly **deferred**: journal watching covers "what happened"; the session API keeps an observer seam so the collector can be added later without breaking changes.

**`DigitalBrain.Testing.Bdd`** (deps: `DigitalBrain.Testing.E2E`, `Reqnroll.xunit.v3`):

- The step vocabulary over `BrainSession`: *user says / user fires X with Y / sees entity / journal shows / corpus contains*. Feature authors write only `.feature` files; a per-scenario hook injects the session.
- The packaged xunit.v3 compat shim (`ITestOutputHelper` shim + `<Compile Remove="obj\**\xUnit3.AssemblyHooks.*.cs" />` via buildTransitive targets) so no test project ever copies shim files. If the pinned Reqnroll/xunit.v3 versions have resolved the incompatibility by implementation time, the shim is omitted.
- The corpus loader (shared parsing with the AI module's mock client, §7).

**Test-mode contract:** the single config key `DigitalBrain:Mode=Testing`, stamped by the SDK via a `WithBrainTestMode()` env projection. Silos read it through `IConfiguration` only — never process environment. This key gates the mock-LLM swap and any other test-mode behavior.

### 6.2 Test project layout & performance rules

```
src/Modules/Time/Tests/DigitalBrain.Modules.Time.Tests/       Tier 2 (Simulation)
src/Modules/Time/Tests/DigitalBrain.Modules.Time.E2E.Tests/   Tier 3 (minimal composed host) — only where warranted
```

- One fixture instance per assembly (collection fixture); a per-module e2e assembly boots only its own slice.
- Sessions isolate by owner id; shared-fixture tests clean up in `try/finally` (owner-scoped state in ephemeral infra may be orphaned — accepted and documented).
- All test projects are MTP exe projects (`UseMicrosoftTestingPlatformRunner`, `TestingPlatformDotnetTestSupport`).

## 7. BDD + mock LLM (one artifact, three jobs)

`.feature` files under module `Tests/` folders are simultaneously:

1. **Executable tests** — Reqnroll runs them through the `DigitalBrain.Testing.Bdd` step library against a `BrainSession`.
2. **The deterministic LLM** — in test mode, the AI module's `IChatClient` factory swaps to a BDD mock that loads the same files at silo startup: the quoted text of the first `Given` becomes a regex prompt pattern, the quoted text of the first `Then` becomes the reply; `Scenario Outline` Examples rows expand one scenario each. An unmatched prompt throws a mock-miss exception naming the prompt (loud, actionable). The mock lives in the **AI module's runtime assembly**, gated by `DigitalBrain:Mode=Testing` — silos cannot reference test packages.
3. **The routing corpus** — scenario prompts feed the semantic capability index (§8), so routing is exercised by the same corpus the tests assert against.

Anti-rot: a meta-test greps step-binding sources for tolerant asserts (`Assert.True(true` and equivalents) and fails above a ceiling that is only ever ratcheted down (start 0).

## 8. Vector routing (MVP scope)

- Kernel defines a narrow **semantic-index capability contract** (index capability descriptions + query by intent); the kernel itself takes **no embedding dependency**.
- The **Memory module** implements it over Qdrant. Capability routing consults it when the module is installed and falls back to the lexical `CapabilityIndex` otherwise. Routing outcomes surface as `RouteOutcome` on the graph.
- BDD scenario prompts are the indexed corpus for capability routing in the MVP.
- Testing: `MockEmbeddingGenerator` (deterministic vectors) ships in `DigitalBrain.Testing`; the real Qdrant path is covered only in Memory's Tier 3 suite.
- Recall-over-corpus and entity search are explicitly out of the MVP; the corpus's watermark makes a trailing indexer possible later without redesign.

## 9. MVP vertical slice (phase 3 exit criterion)

One flow exercising every ratified concept, green at all three tiers:

> Owner activates the brain → chats "plot these values" through the kernel edge → the agent neuron (mock LLM) interprets → **vector routing** resolves the chart capability (lexical fallback covered too) → the handling neuron writes `ChartEntity` → the session neuron's Outgoing journal streams the flow to the watching client → corpus appends the story fact → headless Flutter web UI renders the chart.

Tier coverage: Tier 1 asserts the topology that hosts the flow; Tier 2 pins journal semantics (resume, reset-snapshot at the 512 boundary, tallies, checkpoint/restore, watcher-drop), entity write/read, routing with mock embeddings; Tier 3 runs the full flow through the production AppHost including the BDD feature, the edge, and the UI screenshot/semantics assertion.

## 10. CI & local workflow

- **PR pipeline:** `dotnet build DigitalBrain.slnx -warnaserror` + Tier 1 + Tier 2. Linux, no containers, fast.
- **E2E pipeline:** Tier 3 on Ubuntu with Docker (Azurite, Qdrant), path-filtered + nightly, matrix-sharded per module test assembly, `timeout-minutes` guard, resource-log artifacts uploaded `if: always()`, Playwright installed via the MSBuild target. A manual/nightly **Windows job** covers the desktop-window UI mode.
- **Local:** `scripts/test.ps1` with tier/module filtering (`-Tier model|sim|e2e`, `-Module Time`); `aspire run` remains the manual smoke path, and the aspire CLI/MCP tooling is used to verify the app model during development.

## 11. Phases

| Phase | Deliverable | Exit criterion |
|---|---|---|
| 0 | Mechanical repair: references, `aspire.config.json`, restore `master` Kernel sources as-is, name-constant linked file, retire seed project | Solution builds `-warnaserror`; `aspire run` boots |
| 1 | Core ratification: `Entity<T>`, Cell fold, `Signal` removal, durable-state renames, facade `GetEntity`, journal contract + `docs/JOURNALS.md` | Build green; contracts reviewed |
| 2 | Testing SDK: `DigitalBrain.Testing` + `DigitalBrain.Testing.E2E` packages, Tier 1 conformance suite, Simulation, E2E fixtures + `BrainSession`, test-mode contract (`.Bdd` package moves to phase 3 with its first real consumers; `BrainTestHost.Compose` + fake-server toolkit + Playwright move to phases 3–5) | Conformance + a smoke test per tier green |
| 3 | MVP vertical slice (§9) incl. vector routing, mock LLM, BDD, and the headless-UI harness the slice needs | The §9 flow green at all three tiers |
| 4 | Module test waves: `Tests/` folders for all seven modules; OAuth/MCP fakes; full UI e2e beyond the MVP slice | Every module has Tier 2 coverage; e2e where warranted; CI pipelines live |
| 5 | Community surface: `dotnet new digitalbrain-module` template (module + contracts + hosting + Tests scaffold incl. test silo), NuGet packing, docs | Template produces a building, testing module against packed SDK |

Each phase gets its own implementation plan (via the planning workflow) before code is written. Phases 0–1 land before any SDK code because the SDK compiles against the ratified contracts.

## 12. Risks & mitigations

- **Preview-version churn** (Aspire 13.5-preview, .NET 11 preview, Orleans journaling alpha): pin via central package management; the Tier 1 suite is the canary — it breaks first and cheapest. Latest versions are verified against current docs at each phase start.
- **Reqnroll × xunit.v3 incompatibility**: known shim exists (ino); packaged once in `DigitalBrain.Testing.Bdd`; re-evaluated at pinned versions before phase 3.
- **Ratification breaks `master` behavior silently**: phase 1 lands with the Tier 2 journal-semantics suite in the same phase-2 window — the first SDK deliverable pins the contracts the refactor just ratified.
- **E2E flakiness on shared fixtures**: owner-scoped sessions, health-gated starts, port randomization, and the "Saw: …" diagnostics are all designed-in; per-module minimal slices bound the blast radius.
- **Flutter/CanvasKit assertions**: DOM queries don't work on canvas; primary assertions are wire-level and semantics-tree, screenshots as evidence — the browser is never the only proof.
