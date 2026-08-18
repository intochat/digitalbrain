# Phase 3: MVP Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The spec §9 flow green at all three tiers: owner chats "plot these values" through the kernel edge → the assistant (BDD-scripted mock LLM in test mode) calls the real `fire` tool → capability routing (now hybrid vector+lexical) dispatches `ui.chart-point` → the chart neuron (grants intact) writes a `ChartEntity` → a `ChartCard` produces `Responded.Charts` → SSE streams the chart offer → corpus appends the story fact → UI evidence captured headless.

**Architecture:** Only the LLM is mocked — the mock `IChatClient` (gated on `DigitalBrainNames.Mode`, living in the AI module) emits `FunctionCallContent` per the BDD corpus, and the production `UseFunctionInvocation` pipeline executes the real `find_capabilities`/`fire` tools, so routing, synapse dispatch, entity writes, and SSE all run production code. Vector routing is completed by registering an `IEmbeddingGenerator` (the already-written `CapabilityIndex.FindAsync` hybrid + fallback ladder then just works). Chart state moves into `ChartEntity`; the chart *neuron* keeps the synapse membrane and grants and delegates state (ratified: neurons drive entity writes; reads via `GetEntity` are free, the grants-guarded `IChart.Read` path stays for MCP).

**Tech Stack:** existing pins + three new: `Gherkin` (corpus parser, AI module runtime), `Microsoft.Playwright` (UI evidence), `Reqnroll.Tools.MsBuild.Generation` (feature codegen). Resolve each **latest stable** with `dotnet package search <id> --take 1` at implementation time and add to `Directory.Packages.props`.

**Spec:** `docs/superpowers/specs/2026-08-18-digitalbrain-aspire-testing-sdk-design.md` (§7 BDD, §8 vector routing, §9 slice, §11 phase 3 row). Two amendments land in Task 9: §8 (routing needs only `IEmbeddingGenerator` — `CapabilityIndex` is already hybrid; Qdrant remains the Memory module's concern, untouched this phase) and §9/§11 (UI evidence = wire-level SSE assertion required + env-gated headless screenshot, since Flutter renders to canvas with no semantics layer today).

**Fact base:** the phase-3 surface map (agent report, 2026-08-18) — key anchors: chat edge `/owner/commands` Kind=`chat.send` → SSE `chat-delta` (`MapOwnerCommands.cs:59-63,179-220`); turn pipeline `Chat.cs:807-925` → `ChatTurnWorker.cs:145-174` → `Assistant` (`[FromKeyedServices(typeof(Gemma4))]`, `Assistant.cs:10-11`); keyed `IChatClient` registrations `AIClients.cs:21-42`; mock seam `AIModule.Configure` (`AIModule.cs:11-23`); `CapabilityIndex.FindAsync` hybrid + fallback (`CapabilityIndex.cs:77-151`); `IEmbeddingGenerator` never registered; `Responded.Charts` never populated — `TimerCard` pattern to mirror (`Chat.cs:195-209`); chart today `ChartNeuron.cs` (grants at `:28,40`); corpus producers only `ScheduleNeuron.cs:254`/`BehaviorNeuron.cs:80`; `WithWebHost` exists (`ShellHostingExtensions.cs:29-42`), `WithHeadlessHost` broken (missing `bin/digitalbrain_host.dart`), AppHost uses `WithWindowHost` (`AppHost.cs:31`); `KitChart` has no `Semantics`, only `Key('kit_chart_<title>')`; alias swap bug `ChatChartOffer.cs:4`/`ChatChartPoint.cs:4`.

## Global Constraints

- `E:\intochat\digitalbrain`, branch `finalv2` (HEAD `c3404abd`, builds green; suites 17/17, 5/5, 2/2). NEVER read or write any path under `C:\Users\`.
- Central package management: new pins go in `Directory.Packages.props` (latest stable via `dotnet package search`); references stay bare.
- Build gate per task: `dotnet build DigitalBrain.slnx -warnaserror` → exit 0 (timeout 600000). Suites re-run where a task says so. TDD applies where a task creates test-observable behavior: the task orders test-first steps explicitly.
- Untouchables: domain-journal vocabulary; `DigitalBrainNames` values; the pinned owner-wall/entity semantics (tests exist — changing them is a spec event, not a fix).
- Commits per task, two `-m` flags, trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. No meaningless `/// <summary>`; fix analyzers in code; no suppressions beyond the established `ORLEANSEXP001`-family conventions if a new Orleans experimental surface is consumed (mirror existing csproj precedent, never invent).
- The mock LLM and corpus loader live in the AI module runtime (`src/Modules/AI/AI`), gated on `builder.Configuration[DigitalBrainNames.Mode] == DigitalBrainNames.TestingMode` — production behavior byte-identical when the key is absent.

---

### Task 1: New pins + chart-alias bug fix + `AppHost:UiHost` flag

**Files:**
- Modify: `Directory.Packages.props` (add `Gherkin`, `Microsoft.Playwright`, `Reqnroll.Tools.MsBuild.Generation` at latest stable)
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ChatChartOffer.cs` (alias `"ui.chat-chart"` stays) and `ChatChartPoint.cs` (alias `"ui.chat-chart-offer"` → `"ui.chat-chart-point"` — fixes the swapped-alias latent bug before anything bakes it in)
- Modify: `src/Aspire/DigitalBrain.AppHost/AppHost.cs` (~line 31): read `builder.Configuration["AppHost:UiHost"]` — `"web"` → `ui.WithWebHost()`, anything else/absent → `ui.WithWindowHost()` (current default preserved; one small conditional, comment: `// AppHost:UiHost=web selects the headless-web shell (e2e evidence); default stays the desktop window.`)

**Interfaces:** Produces the three pins later tasks reference; the `AppHost:UiHost` flag Task 8 passes as `--AppHost:UiHost=web`.

- [ ] **Step 1:** `dotnet package search Gherkin --take 1` (likewise Playwright, Reqnroll.Tools.MsBuild.Generation); add `<PackageVersion>` entries with the found versions.
- [ ] **Step 2:** Alias fix + AppHost conditional as above.
- [ ] **Step 3:** Build gate; `dotnet test tests/DigitalBrain.Aspire.Tests -c Debug` → 17/17 (topology unchanged under the default flag).
- [ ] **Step 4:** Commit: `"Pin corpus/UI-evidence packages; fix chart alias swap; add AppHost UiHost flag"`.

---

### Task 2: Test-mode gate in the AI module — BDD mock chat client + mock embeddings

**Files:**
- Create: `src/Modules/AI/AI/Testing/BddScenarioCorpus.cs` (Gherkin `.feature` loader)
- Create: `src/Modules/AI/AI/Testing/BddMockChatClient.cs`
- Create: `src/Modules/AI/AI/Testing/DeterministicEmbeddingGenerator.cs`
- Create: `src/Modules/AI/AI/Testing/MockLlmMissException.cs`
- Modify: `src/Modules/AI/AI/AIModule.cs` (the gate)
- Modify: `src/Modules/AI/AI/DigitalBrain.Modules.AI.csproj` (bare `Gherkin` reference)

**Interfaces:**
- Consumes: `DigitalBrainNames.Mode`/`TestingMode`; `AIClients.Add` keyed registration shape (`AddKeyedSingleton<IChatClient>(typeof(TModel), ...)` for `Llama32`,`Gemma4`,`Qwen35`,`Granite41`,`Gpt56` — read `AIClients.cs:21-30` for the exact type list); `IChatClient` (GetResponseAsync/GetStreamingResponseAsync/GetService) and `FunctionCallContent`/`FunctionResultContent`/`TextContent` from Microsoft.Extensions.AI 10.8.3; the `fire` tool contract (`SystemTools.Fire = "fire"`, args: `contract`, `arguments` JSON, optional `target` — read `SystemTools.FireCoreAsync` for exact parameter names).
- Produces: config key `DigitalBrain:AI:Corpus:Path` (directory of `.feature` files; the gate throws a clear error when test mode is on and the path is missing/empty); corpus scenario grammar (below); `MockLlmMissException` naming the unmatched prompt and listing loaded scenario patterns.

**Corpus grammar** (one scenario = one scripted turn):
```gherkin
Scenario: plot request
  Given the user says "plot these values: (?<rest>.*)"
  When the assistant fires "ui.chart-point" at "chart/demo" with {"Series":"demo","Label":"a","Value":1}
  When the assistant fires "ui.chart-point" at "chart/demo" with {"Series":"demo","Label":"b","Value":3}
  When the assistant fires "ui.chart-card" at the chat with {"Title":"demo"}
  Then the assistant replies "Plotted 2 points on 'demo'."
```
- `Given` quoted text = regex matched (ordinal, singleline) against the LAST user-role message's text.
- Each `When ... fires "X" ... with {json}` emits one `FunctionCallContent` invoking the **`fire`** tool with `contract = X` and `arguments` = the JSON object serialized to the string shape `fire` expects. The optional `at "<grainType>/<instance>"` clause passes the tool's `target` argument (read `SystemTools.ResolveTarget` for the exact accepted target string shape and use that). The `at the chat` form targets the chat's neuron id, which the mock finds in the system message `Agent`/`ChatTurnWorker` prepends (read `ChatTurnWorker.cs:157-160` for the exact system-message wording and parse the chat neuron id out of it; quote the format in code comments). INVARIANT the corpus must honor: the `ChartCard.Title` names the chart instance the points were fired at (`"demo"` in both places above) — the ChartCard handler reads the entity by that name.
- `Then` quoted text = the final assistant text after all function results return.
- Mock behavior across the function-invocation loop: on each call, if the last message is a `FunctionResultContent`, emit the NEXT scripted call (or the final text when the script is exhausted); `GetStreamingResponseAsync` yields the same content as single updates (read `Agent.RespondStreaming`'s consumption in `Agent.cs:37-79` first and mirror what it needs).
- No scenario matches → throw `MockLlmMissException` (loud, actionable).

**The gate** (`AIModule.Configure`, before `AIClients.Add`):
```csharp
if (string.Equals(builder.Configuration[DigitalBrainNames.Mode], DigitalBrainNames.TestingMode, StringComparison.Ordinal))
{
    AITestingClients.Add(builder.Services, builder.Configuration);  // keyed mocks for every model type + IEmbeddingGenerator
}
else
{
    AIClients.Add(builder.Services);
}
```
(`AITestingClients.Add` registers the SAME keyed `IChatClient` set — every model `Type` — all resolving one shared `BddMockChatClient`, plus `services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new DeterministicEmbeddingGenerator())`, plus the unkeyed `TryAddSingleton` fallback mirroring `AIModule.cs:21-22`. It must NOT register `LlmWarmupHostedService` and must not touch Ollama config — that's what makes test hosts boot without containers.)

`DeterministicEmbeddingGenerator`: hash-based — token-bag hashing into a fixed 64-dim vector (per-token `xxHash`/`GetHashCode`-seeded bucket += 1, then L2-normalize) so similar texts share buckets; document determinism as the ONLY guarantee.

- [ ] **Step 1:** Write the four files + gate; wire csproj.
- [ ] **Step 2:** Build gate → exit 0.
- [ ] **Step 3 (first runtime consumer proof):** `dotnet test tests/DigitalBrain.Simulation.Tests -c Debug` → 5/5 (sim doesn't load the AI module yet — unchanged; Task 6 adds coverage).
- [ ] **Step 4:** Commit: `"Gate the AI module on test mode with a BDD-scripted mock chat client"`.

---

### Task 3: Production embeddings opt-in (completes vector routing)

**Files:**
- Modify: `src/Modules/AI/AI/Clients/AIClients.cs` (register `IEmbeddingGenerator` when `DigitalBrain:AI:Ollama:Embeddings:Model` is configured — OllamaSharp 5.4.30 implements the MEAI embedding abstractions; read its API from the package the repo already uses in `AIClients.Ollama` and mirror the client construction)
- Modify: `src/Modules/AI/Aspire.Hosting/AIHostingExtensions.cs` (add `WithEmbeddings(string model = "nomic-embed-text")` to the AI module builder: `.AddModel(...)` on the existing Ollama resource + env projection `DigitalBrain__AI__Ollama__Embeddings__Model`)
- Modify: `src/Aspire/DigitalBrain.AppHost/AppHost.cs` (~line 25 chain: add `.WithEmbeddings()` to the AI module configuration)

**Interfaces:**
- Consumes: existing `AddOllamaModel` config-read pattern (`AIClients.cs:35-58`); the AI hosting projection model (`AIHostingExtensions.cs` — read the `WithLlm<T>` implementation and mirror).
- Produces: `IEmbeddingGenerator<string, Embedding<float>>` registered in production silos when the config key is present → `CapabilityIndex.FindAsync` (`CapabilityIndex.cs:77-151`) and `SystemTools.FindCapabilitiesAsync` (`SystemTools.cs:52-62`) switch to hybrid ranking automatically; absent key → lexical fallback (already built-in, `CapabilityIndex.cs:84-87`). NO changes to `CapabilityIndex` or the Memory module.

- [ ] **Step 1:** Implement all three edits (config-gated: no key → no registration → no behavior change).
- [ ] **Step 2:** Build gate; `dotnet test tests/DigitalBrain.Aspire.Tests -c Debug` → all green (add one Tier 1 fact IN THIS TASK first — TDD: kernel rendered env contains `DigitalBrain__AI__Ollama__Embeddings__Model` — write it, see it fail, then wire Step 1, see it pass).
- [ ] **Step 3:** Commit: `"Register Ollama embeddings opt-in, completing hybrid capability routing"`.

---

### Task 4: `ChartEntity` + chart neuron delegation + `ChartCard` → `Responded.Charts`

**Files:**
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chart/IChartEntity.cs` + `ChartState.cs`
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ChartCard.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/IChat.cs` (add `IHandle<ChartCard>`)
- Create: `src/Modules/UI/DigitalBrain.Modules.UI/Chart/ChartEntity.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/Chart/ChartNeuron.cs` (state → entity delegation)
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` (ChartCard handler, mirroring the TimerCard handler at `:195-209`)

**Interfaces:**
- Consumes: `Entity<TState>`/`IEntity<TState>` (phase 1), `EntityId.For<TEntity>`, the `[GrainType]` convention (`GrainTypeNames.Of(typeof(IChartEntity))` = `"chartentity"` — pinned by the phase-2 counter test), grants helper `GrantsNeuron.RequireReadAccessAsync` (`Core/Grants/GrantsNeuron.cs:101`), the fixed aliases from Task 1.
- Produces:
```csharp
[GenerateSerializer] [Alias("ui.chart-state")]
public sealed record ChartState(
    [property: Id(0)] IReadOnlyList<ChartStatePoint> Points);
[GenerateSerializer] [Alias("ui.chart-state-point")]
public sealed record ChartStatePoint([property: Id(0)] string Series, [property: Id(1)] string Label, [property: Id(2)] double Value);

[Alias("ui.chart-entity")]
public interface IChartEntity : IEntity<ChartState> { }

[GenerateSerializer] [Alias("ui.chart-card")]
public sealed record ChartCard([property: Id(0)] string Title) : Synapse;
```
  `ChartEntity : Entity<ChartState>, IChartEntity` with `[GrainType("chartentity")]` and an internal `Append(ChartStatePoint point, int cap)` method (adds, trims to 256, `SaveAsync`) — read `Entity.cs` for the exact `SaveAsync` signature. Entity has no synapse membrane; `Append` is a plain grain method the neuron calls (attributed grain-to-grain — passes the owner wall same-owner). Declare `Append` on `IChartEntity` (NOT `[ClientEntryPoint]` — the concrete contract carries no entry-point attribute, so `Append` stays unreachable to external clients while `Read()` inherits the base contract's entry point; state this in a comment).
- `ChartNeuron` changes: keep `[GrainType("chart")]`, `IHandle<ChartPoint>`, and BOTH grants checks; `HandleAsync` now calls `GrainFactory.GetGrain<IChartEntity>(EntityId.For<IChartEntity>(Id.Owner, Id.Name).ToGrainId()).Append(...)` instead of the durable list; `Read()` delegates to the entity's `Read()` and projects `ChartState.Points` back to `IReadOnlyList<ChartPoint>` — **`IChart`'s surface is unchanged**, so `RegistryTools.read_chart`, the capability manifest (`ui.chart-point` in `Accepted`), graph validation, and the Assistant prompt all keep working (this is what makes the naive-conversion breakage list moot).
- `Chat` ChartCard handler (mirror the TimerCard handler at `Chat.cs:195-209` byte-for-byte in structure — read it first): on `ChartCard`, load the chart data via `GrainFactory.GetGrain<IChartEntity>(EntityId.For<IChartEntity>(Id.Owner, card.Title).ToGrainId()).Read()` (invariant: `Title` names the chart instance the points targeted — see the corpus grammar), build `ChatChartOffer(card.Title, points → ChatChartPoint(Label, Value), "bar")`, `Remember` the turn fragment the way TimerCard does, emit `Responded(..., Charts: offers, ...)`.

- [ ] **Step 1 (TDD):** In `tests/DigitalBrain.Simulation.Tests`, add `ChartFlowTests` (new file; fixture gains UI+AI+Execution module assemblies ONLY if needed — for THIS test, fire `ChartPoint` directly at the chart neuron via the facade and assert `GetEntity<IChartEntity>` returns the state; that needs the UI module implementation + contracts assemblies in `ModuleAssemblies` — extend the existing `SimulationFixture` accordingly). Write the test, run → RED (types missing).
- [ ] **Step 2:** Implement the files above. Build gate.
- [ ] **Step 3:** `dotnet test tests/DigitalBrain.Simulation.Tests -c Debug` → GREEN (all, incl. the 5 existing).
- [ ] **Step 4:** Commit: `"Move chart state into ChartEntity; wire ChartCard offers into Responded"`.

---

### Task 5: Corpus story facts on the chat path

**Files:**
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` (`TryEmitRespondedAsync`, `:780-802`)

**Interfaces:**
- Consumes: `AppendCorpusEntry(CommandId, Kind, Text, Correlation, At)` + `ICorpus.ForOwner(Id.Owner)`; the fire pattern from `ScheduleNeuron.cs:254-255` (mirror it).
- Produces: every completed turn appends `AppendCorpusEntry(CommandId.New(), "chat.responded", <answer text>, Correlation: <turn CommandId string>)` — making `ReadEpisode` the per-turn story query.

- [ ] **Step 1 (TDD):** Simulation test: drive a chart-flow turn's `Responded` (or call the chat's Send with the mock LLM if Task 6's fixture is ready; otherwise fire the minimal synapse path that reaches `TryEmitRespondedAsync` — read `Chat.cs` to choose the cheapest deterministic trigger, e.g. the `TimerCard` handler which emits `Responded` directly) → assert `brain.Get<ICorpus>(...)`/`ReadCorpus` contains the `chat.responded` entry. RED first.
- [ ] **Step 2:** Implement the append (fire-and-forget via the neuron's `SendAsync` mirror of `ScheduleNeuron`; do not block the turn on corpus). Build; sim suite GREEN.
- [ ] **Step 3:** Commit: `"Append chat turns to the owner corpus"`.

---

### Task 6: `DigitalBrain.Testing.Bdd` package + the MVP corpus + Tier 2 full-turn test

**Files:**
- Create: `src/Testing/DigitalBrain.Testing.Bdd/DigitalBrain.Testing.Bdd.csproj` (packable; refs: `DigitalBrain.Testing.E2E` project; bare `Reqnroll.xunit.v3` + `Reqnroll.Tools.MsBuild.Generation`)
- Create: `src/Testing/DigitalBrain.Testing.Bdd/BrainSteps.cs` (step library over `BrainSession` + kernel HTTP edge)
- Create: `tests/corpus/mvp-chart.feature` (the §9 scenario in the Task 2 grammar — this ONE file is simultaneously the mock-LLM script and, in Task 8, the executed e2e feature)
- Modify: `tests/DigitalBrain.Simulation.Tests` (fixture: add AI + Execution module assemblies + `DigitalBrain:AI:Corpus:Path` config pointing at `tests/corpus`; new `ChatTurnTests`)
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: Task 2's corpus grammar + config key; `BrainSession` (phase 2); the chat edge DTOs (`OwnerCommandRequest` fields, `HttpSurfacePaths.KindChatSend`, SSE `chat-delta` — read `MapOwnerCommands.cs` for shapes); `ICorpus` read synapses.
- Produces: step vocabulary (initial set, exact bindings):
  - `Given the owner opens the chat "(.*)"` → session + chat name state
  - `When the owner says "(.*)"` → POST `/owner/commands` Kind=`chat.send` and drain the SSE response
  - `Then the chat offers a chart titled "(.*)"` → GET `/chats/{chat}/events` SSE until a `ChatTurnEvent` with a chart of that title (bounded wait, "Saw:" diagnostics)
  - `Then the chart entity "(.*)" holds (\d+) points?` → `session.Brain.GetEntity<IChartEntity>(name).Read()`
  - `Then the corpus records a "(.*)" fact for the turn` → fire `ReadCorpus`/`ReadEpisode` via the facade
  (Reqnroll + xunit.v3 codegen: if the generated hooks fail to compile at the pinned versions, port the `ReqnrollXunitV3Compat` shim pattern from `E:\intochat\Projects\ino\src\Ino.NeuronTesting.Bdd\ReqnrollXunitV3Compat.cs` — read-only reference access allowed — INTO the Bdd package with a comment naming the incompatibility; budget for this, it is a known friction point.)
- Tier 2 `ChatTurnTests` (plain xUnit, not Reqnroll — the Bdd package is exercised in Tier 3): simulation with UI+AI+Execution+Time modules + test-mode config (`DigitalBrainNames.Mode=Testing` via `ConfigureSilo`/host configuration — read how `BrainSimulationOptions.ConfigureSilo` can inject configuration, extend `BrainSimulationOptions` with a `Configuration` dictionary if needed) + corpus path; drive `brain.GetGrainProxy<IChat>("main").Send(new SendMessage(...))` (read `IChat.cs`/`SendMessage` for the exact ctor); assert: chart entity holds the scripted points, `Responded.Charts` observed on the chat's Outgoing journal via `JournalWait`, corpus episode present.

- [ ] **Step 1:** Corpus file + fixture config; `ChatTurnTests` written first → RED (mock not reachable until config lands), then wire, then GREEN.
- [ ] **Step 2:** Bdd package + steps; build gate (codegen compiles even with zero features in the package itself).
- [ ] **Step 3:** Full sim suite GREEN; build gate.
- [ ] **Step 4:** Commit: `"Add the BDD step library and the MVP corpus; prove the scripted turn in simulation"`.

---

### Task 7: Tier 2 routing + embedding tests

**Files:**
- Create: `tests/DigitalBrain.Simulation.Tests/RoutingTests.cs`

**Interfaces:** Consumes `CapabilityIndex` (`Build`, `Find`, `FindAsync`), `DeterministicEmbeddingGenerator`, the module manifests already loaded by the fixture.

- [ ] **Step 1 (tests only — the code exists):**
  - `FindAsync` with the deterministic generator ranks `ui.chart-point` in the top hits for "plot these values" (hybrid path — assert it differs from or at least matches lexical, and that no exception surfaces).
  - `FindAsync` with `embeddings: null` returns the lexical result (fallback pinned).
  - `DeterministicEmbeddingGenerator` is deterministic (same text → identical vector) and normalized.
- [ ] **Step 2:** Sim suite GREEN; build gate; commit: `"Pin hybrid capability routing and the deterministic embedding contract"`.

---

### Task 8: Tier 3 — the §9 flow end-to-end + UI evidence

**Files:**
- Modify: `tests/DigitalBrain.E2E.Tests` (csproj: add `DigitalBrain.Testing.Bdd` ref + `Reqnroll` codegen; `Features/MvpChart.feature` — a link/copy of `tests/corpus/mvp-chart.feature` executed by the Task 6 steps; `UiEvidenceTests.cs`)
- Modify: `src/Testing/DigitalBrain.Testing.E2E` IF the fixture needs a corpus-path/test-config projection (e.g. `BrainE2EOptions.ProjectEnvironment` dictionary stamped onto project resources — smallest addition that gets `DigitalBrain__AI__Corpus__Path` to the kernel)

**Interfaces:**
- Consumes: everything above; fixture args gain `--AppHost:UiHost=web` ONLY in the UI-evidence collection (default e2e keeps the flutter resource explicit-started and never boots it).
- Produces:
  - The executed BDD feature: owner says "plot these values: …" over real HTTP → SSE chart offer → `GetEntity` state → corpus episode. This is the REQUIRED Tier 3 assertion set (wire-level = what the UI consumes).
  - `UiEvidenceTests` (separate collection, `[Trait("Category","UiEvidence")]`, **skipped unless `DIGITALBRAIN_UI_EVIDENCE=1`**): boots with `--AppHost:UiHost=web`, waits for the flutter web resource's endpoint, drives Playwright headless Chromium to the shell URL, logs in with the shell's default dev credentials (read `host_environment.dart:12-15`), sends the chat message through the UI, waits ~for the canvas to settle (`WaitUntilState.Load`, never `NetworkIdle` — OTLP keeps requests open), captures a screenshot to the test output directory as evidence. Canvas rendering means NO DOM assertion on the chart — the screenshot is evidence, the wire-level feature is proof; state this in a comment. Playwright browser install: MSBuild target in the e2e csproj running `playwright.ps1 install chromium` with `ContinueOnError` (ino's pattern).
- [ ] **Step 1:** Feature + steps wiring; run `dotnet test tests/DigitalBrain.E2E.Tests -c Debug` (Docker running) → BDD feature GREEN (2 existing + feature scenarios).
- [ ] **Step 2:** UI evidence test (env-gated); run once locally WITH the env var to capture the screenshot; report its path + whether the chart is visible. If the flutter web boot proves unstable after ONE debugging pass, mark the test `Skip` with the reason and report — do not sink unbounded time.
- [ ] **Step 3:** Build gate; commit: `"Run the MVP slice end-to-end over HTTP with headless UI evidence"`.

---

### Task 9: Full gates + spec amendments

**Files:** spec (§8, §9/§11 amendments per the plan header); no code.

- [ ] **Step 1:** All four suites green (`Aspire`, `Simulation`, `E2E` incl. feature; UI evidence env-gated run optional). Record counts + durations.
- [ ] **Step 2:** Spec §8: routing = `IEmbeddingGenerator` + existing hybrid `CapabilityIndex` (lexical fallback), Qdrant unchanged in Memory; §9/§11: UI assertion = wire-level SSE (required) + env-gated headless screenshot (evidence); note the corpus file's dual role (mock script + executed feature).
- [ ] **Step 3:** Build gate; commit: `"Amend spec for phase 3 routing and UI-evidence rulings"`.
