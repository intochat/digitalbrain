# Neuron E2E — ino's test strategy

**Status:** open architectural discussion. This file is a continuation prompt for a fresh Claude session — read top to bottom, then pick up where §8 leaves off.

**Date written:** 2026-05-07
**Working tree:** `E:\ino`, branch `master`
**Project rules to honor:** `E:\ino\CLAUDE.md` (especially: latest NuGet, no /// docstrings, Context7 for any library API, never touch `C:\Users\…`).

---

## 1. The premise (don't relitigate this — it's the user's call)

ino is an operating system built from domains that contain two primitives:

- **Neurons** — Orleans grains. Some pure-code (`Neuron<TEvent>`), some LLM-backed (`LlmNeuron<TEvent>` : `IAW.Core.Agent`).
- **Synapses** — typed, durable messages between neurons; signal + memory + thinking unified.

There is no third runtime/product unit between domains and neurons. Any old naming that implies one is legacy and should be removed during refactor.

**ino's test strategy is e2e-only.** Every neuron is covered by one or many e2e tests, where "many" scales with the neuron's surface area. There are essentially no unit tests at the ino layer — if a neuron's behavior is worth asserting, the assertion belongs in an e2e test that proves the *user-visible outcome* (a Flutter-rendered RFW card, or a synapse fired into another neuron's inbox).

**IAW** (the substrate at `iaw/`) is allowed to have other test types — it's the test-host configuration provider for ino. IAW already ships `iaw/src/Testing/AgentTest.cs` with a `TestCluster` + `MockChatClient`; ino's e2e fixtures build on top.

The user has explicitly rejected:
- "Vertical slice per neuron with a sub-csproj per neuron" — too much Orleans / Aspire packaging churn.
- A renamed-but-otherwise-unchanged old browser test base. The new name is **`NeuronE2ETest<TNeuron>`** because *neuron* is the real unit of work.
- Multi-tier test ladders (Tier 0/1/1.5/2/3). Just e2e.

**Acceptance shape for the strategy as a whole:** after all neuron e2e tests are green, `aspire run` from a clean checkout boots the system, the user types "plan a trip to Tokyo next week", and a real LLM produces real RFW card responses without further fixing. If green tests don't deliver that, the tests aren't testing the right thing.

---

## 2. Anatomy of a NeuronE2ETest

A neuron e2e test starts a **real Aspire AppHost** (kernel + identity + relevant domain silos), drives a real prompt or synapse fire end-to-end, and asserts that the **Flutter client renders the expected RFW card** in the browser. The LLM is mocked via the existing BDD `.feature` corpus (deterministic, scripted) with a **100 ms response delay** so streaming patterns (skeleton-then-data) are observable.

### What "covered" means

For neuron `TripPlannerNeuron`, the test set covers:

1. **Happy path** — `?q=plan a trip to Tokyo next week` → six-hop flow → `ino.travel.summary` card visible in the browser.
2. **Slot-missing path** — `?q=plan a trip to Tokyo` (no dates) → `ask_clarification` chip-row card visible.
3. **Branch path** — `events.skipped` synapse fires → activities card still arrives.
4. **Failure surface** — invalid destination → graceful error card (not a crash).

A simple neuron may have one e2e. A neuron with rich branching (`TripPlannerNeuron`, `CortexNeuron`) has several. Number of tests ≈ number of distinct user-visible outcomes the neuron can produce.

### The base class (proposed)

```csharp
// src/Ino.Testing.E2E/NeuronE2ETest.cs
public abstract class NeuronE2ETest<TNeuron>
    : IClassFixture<InoBrowserFixture<Projects.Ino_AppHost>>
    where TNeuron : class
{
    protected InoBrowserFixture<Projects.Ino_AppHost> Fx { get; }
    protected GrpcCapture Capture { get; }   // auto-attached, filters by content_type
    protected BrainTraceProbe Brain { get; } // see §6

    protected Task SendAsync(string prompt);                                  // ?q= deep-link
    protected Task<RfwFrame> ExpectCardAsync(string contentType, int ms = 30_000);
    protected Task<RfwFrame> FireRfwEventAsync(string corrId, string evt, IDictionary<string,string>? args = null);
    protected Task ScreenshotAsync(string name);  // -> reviews/<TNeuron>/<test>.png
    protected Task ExpectFiredSynapsesAsync(params Type[] synapseTypes);
}
```

`TNeuron` is more than a label — the base class introspects its `IHandler<TSynapse>` declarations to pre-filter `Capture` to that neuron's incoming/outgoing wire payloads. Keeps test code declarative.

### What the test code looks like

```csharp
public class TripPlannerNeuronE2ETests : NeuronE2ETest<TripPlannerNeuron>
{
    [Fact]
    public async Task plans_tokyo_trip_renders_intro_then_summary()
    {
        await SendAsync("plan a trip to Tokyo next week");
        await ExpectCardAsync("ino.travel.intro");
        await ExpectFiredSynapsesAsync(typeof(FlightSearch), typeof(HotelSearch));
        await ScreenshotAsync("plan-tokyo-intro");

        // walk to summary
        var corr = Capture.LastCorrelationId;
        await FireRfwEventAsync(corr, "flight.selected",  new() { ["flightId"] = "FL-001" });
        await FireRfwEventAsync(corr, "hotel.selected",   new() { ["hotelId"]  = "H-001"  });
        await FireRfwEventAsync(corr, "events.skipped");
        await FireRfwEventAsync(corr, "activity.selected",new() { ["activityId"] = "AC-001" });

        var summary = await ExpectCardAsync("ino.travel.summary");
        Assert.Contains("Tokyo", summary.Body);
        await ScreenshotAsync("plan-tokyo-summary");
    }
}
```

That's the whole test surface — no Playwright plumbing, no inline gRPC capture class in each neuron test.

### Why end with an actual rendered card

CanvasKit paints to canvas, not DOM. The current tests assert on gRPC response bytes, which is *correct* but doesn't prove the user sees a card. The new contract is:

1. **Wire-level**: gRPC frame with `content_type = "ino.X.Y"` and a non-empty `RfwDescription` arrived. (Existing `GrpcCapture` mechanism.)
2. **Render-level**: a screenshot taken after `ExpectCardAsync` resolves, written to `reviews/<TNeuron>/<test>.png`. Visual evidence, not a pixel-diff assertion.
3. **Optional render-correctness**: enable Flutter semantics with `?semantics=1` and assert key card text appears in the accessibility tree. Slower; reserve for tests that need to prove "user can read the price."

Tier-1.5 Flutter widget tests (loading `Cards/*.rfw` against fixtures) were rejected as a separate tier — fold into the e2e path or drop entirely. **Decision pending.**

---

## 3. ino vs IAW — testing split

| Layer | What it tests | How |
|---|---|---|
| **IAW substrate** | Agent base class, IChatClient pipeline, tool middleware, durable chat history, scheduling | Existing `iaw/src/Testing/AgentTest<TAgent>` — TestCluster + MockChatClient. **Owned by IAW; ino doesn't change it.** |
| **ino** | Every neuron, every synapse path, every RFW card | `NeuronE2ETest<TNeuron>`. Real Aspire AppHost. BDD-mocked LLM. Browser-rendered card. |

**IAW *is* the test-host configuration provider for ino.** Concretely: the silo bootstrap that an ino e2e test boots is `Ino.AppHost`, which calls `AddIno()` → `AddIAW()`. The test cluster Orleans setup is IAW's. ino's testing layer adds:

- The `INO_TEST_MODE=true` env switch that swaps `IChatClientFactory` to `BddMockChatClientFactory`.
- The ephemeral port allocator in `InoTestAppHost`.
- Playwright + the gRPC capture / brain probe helpers.

Open question for the next Claude: **should `Ino.Testing` ProjectRef IAW's `Testing` assembly?** Today it doesn't. If neuron e2e tests need `MockChatClient` directly (rather than going through BddMockChatClientFactory), the ref would let them. Read `iaw/src/Testing/MockChatClient.cs` and decide.

---

## 4. Concrete starting work — where to begin

### 4.1 Fix the failing test first

```bash
dotnet test ino.slnx --filter "Neuron=TripPlanner"
```

is failing. The user's diagnosis: `/brain` is now the homepage (was `/home`). My read of the code (`clients/ino.flutter/lib/screens/brain/brain_home_screen.dart:51-67`):

The `?q=` deep-link consumer sits inside the *same* `addPostFrameCallback` as `_brainStream!.start()`. If the brain stream throws on the silo (e.g., `WatchBrainActivity` server-stream isn't ready, or `BrainInspectorBloc` lookup races BlocProvider), the `SendMessage(q)` line never executes — test times out waiting for a Chat() response that never came.

**Recommended fix:** split the deep-link consumer into its own `addPostFrameCallback` so it cannot be suppressed by an unrelated brain-stream failure. Keep the brain stream running on its own callback.

```dart
// app.dart route '/brain' currently goes to BrainHomeScreen.
// Inside BrainHomeScreen.initState — split callbacks:

WidgetsBinding.instance.addPostFrameCallback((_) {
  if (!mounted) return;
  final q = Uri.base.queryParameters['q'];
  if (q != null && q.isNotEmpty) {
    context.read<PersonaBloc>().add(PersonaEmotionChanged(PersonaEmotion.thinking));
    context.read<InoBloc>().add(SendMessage(q));
  }
});

WidgetsBinding.instance.addPostFrameCallback((_) {
  if (!mounted) return;
  try {
    final stub = InoClient(context.read<InoBloc>().grpcClient.channel);
    _brainStream = BrainStreamService(stub, context.read<BrainInspectorBloc>());
    _brainStream!.start();
    _inspectorSub = context.read<BrainInspectorBloc>().stream.listen(_onInspectorState);
  } catch (e, st) {
    // ignore: avoid_print
    print('[brain] stream init failed: $e\n$st');
  }
});
```

### 4.2 Build `NeuronE2ETest<TNeuron>` once

Add `src/Ino.Testing.E2E/NeuronE2ETest.cs` with the API in §2. Implement:

- `GrpcCapture` extracted from the legacy Plan Trip browser test.
- `BrainTraceProbe` (see §6).
- Helpers above.

Add a 100 ms BDD response delay:

- New option on `BddMockChatClient` constructor: `TimeSpan responseDelay`.
- New env var `INO_MOCK_LLM_DELAY_MS` read by `BddMockChatClientFactory`.
- `InoTestAppHost.InitializeAsync` sets it to `100` by default; tests can override.

### 4.3 Migrate ONE neuron's tests to NeuronE2ETest

Pick `TripPlannerNeuron`. Rewrite the legacy Plan Trip browser/gRPC tests as a single `TripPlannerNeuronE2ETests` using the new base. Delete the old files. Confirm green.

That single migration validates the base class. Don't migrate more until the user signs off on the resulting shape.

### 4.4 Then the rest

One neuron at a time. Each migration deletes the old tests for that neuron. By the time every neuron has e2e coverage, the bulk of the legacy `test/` projects can be deleted wholesale.

---

## 5. Acceptance criteria for the first slice

1. `dotnet test ino.slnx --filter "Neuron=TripPlanner"` passes (after the deep-link fix in §4.1).
2. `NeuronE2ETest<TNeuron>` exists in `Ino.Testing.E2E` with the §2 API.
3. `TripPlannerNeuronE2ETests` exists, passes, **and produces screenshots** under `reviews/TripPlanner/`.
4. The legacy Plan Trip tests are deleted after their coverage has moved.
5. `dotnet test ino.slnx` and `aspire run` both still work end-to-end.
6. Honor `CLAUDE.md` rules: latest NuGet via Context7, no `///` doc-comments, no scripts touching `C:\Users\…`.

---

## 6. Brain — defer, but here's the open architecture question

**The user wants to discuss brain after the e2e foundation is in.** Don't build the brain redesign yet. But the next Claude should be ready to discuss with this context:

### Current state (verified)

- `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs` — **already implements `IIncomingGrainCallFilter`**. Wraps every grain call and emits a `BrainPulse` on the `ino-brain` Orleans stream. Reads caller identity from `RequestContext`.
- `src/Ino.Core/Brain/BrainPulse.cs` — the typed pulse record.
- `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs` — exposes `WatchBrainActivity` server-streaming RPC fed from the brain stream.
- `clients/ino.flutter/lib/services/brain_stream_service.dart` — Flutter consumer; renders into `BrainInspectorBloc`.
- `clients/ino.flutter/lib/screens/brain/brain_home_screen.dart` — three.js 3D scene, picks neurons + synapses on tap.

So **the user's hint about grain call filters is already implemented**. The architecture isn't "should we use filters" — it's "what next."

### The open questions (for discussion, not implementation)

1. **Click-on-neuron drawer is empty today.** `_handleTap` in `brain_home_screen.dart` dispatches `SelectNeuron` for neuron picks, but the inspector drawer renders nothing meaningful for that case. The data is already in `BrainInspectorBloc.recentByNodeId`. Small fix vs. larger UX rethink — discuss.
2. **Per-domain segmentation.** Topology already domain-tints nodes. Open: filter chip row at top? Spatial clustering by domain? Currently nodes are positioned by some other layout — check `clients/ino.flutter/lib/screens/brain/brain_topology.dart`.
3. **Test-trace second tab.** User wants a `Live | Traces` tab pair where Traces lists past test runs and replays the recorded brain pulses for that run. Implies:
   - `BrainTraceFilter` writes to a per-run sink when `INO_TEST_MODE=true`. Currently the sink is the Orleans stream — it's ephemeral. Add a file/blob sink keyed by test id.
   - Flutter UI gets a tab switcher and a trace-list endpoint (`/api/brain-traces`).
   - This is local-dev only by default; not for prod.
4. **Pulse fan-out cost.** `BrainTraceFilter` runs on *every grain call*. There's a 1s emit timeout and 4096-byte payload cap. For a hot path (e.g., `IDurableList` appends inside a chatty neuron) the per-call overhead is real. Profile before scaling neuron counts up.
5. **Self-pulse vs. cross-grain pulse.** The filter currently emits one pulse per *call*. A synapse fire (`FirePort.FireAsync`) is a grain call from grain A to grain B; a self-grain method is also a call. The brain UI conflates these or distinguishes them — verify.

### Files to read before discussing

```
src/Ino.Core/Brain/BrainPulse.cs
src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs
test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs
src/Ino.Gateway.Grpc/Services/InoGrpcService.cs        (look for WatchBrainActivity)
clients/ino.flutter/lib/services/brain_stream_service.dart
clients/ino.flutter/lib/screens/brain/brain_home_screen.dart
clients/ino.flutter/lib/screens/brain/brain_inspector_drawer.dart
clients/ino.flutter/lib/state/brain_inspector_bloc.dart
clients/ino.flutter/lib/screens/brain/brain_topology.dart
test/Ino.E2E.Tests/BrainStreamE2ETests.cs              (existing brain e2e, possibly to fold into NeuronE2ETest)
```

---

## 7. What "trash" to leave alone for now

The user thinks ~99% of tests can collapse into neuron e2e. That's optimistic but directionally right. **Don't do a big-bang delete.** Migrate one neuron at a time; delete *that neuron's* old tests when the new e2e is green. By the end most of `test/` will be gone.

**Do NOT delete yet:**
- `iaw/test/**` — IAW's tests, not ino's.
- `domains/travel/tripradar/**/Tests/**` — TripRadar's own tests, separate product.
- `Ino.Testing.Tests/` and `Ino.Core.Hosting.Tests/BddScenarioLoaderTests` — these test the test infrastructure itself.

**Definitely safe to delete after migration:**
- Any per-class unit test whose target class is internal plumbing of a neuron whose e2e is green.

---

## 8. Where to pick up — prompts for the next Claude

Read this file. Then in priority order:

1. **Confirm the failing test diagnosis in §4.1.** Run the test, read the actual error. Update §4.1 with the real failure mode if my diagnosis was wrong.
2. **Implement §4.1** — the deep-link split. Get the existing test green without touching test code.
3. **Implement §4.2** — `NeuronE2ETest<TNeuron>` base + `GrpcCapture` + 100 ms BDD delay. Don't migrate anything yet.
4. **Implement §4.3** — migrate `TripPlannerNeuron`'s tests. Show the user the diff. Wait for approval before doing more neurons.
5. **Open §6 with the user** — "now let's talk brain." Have the file list in §6 read so you can answer concretely.

### Prompts to NOT engage with (already settled)

- "Should we have a multi-tier test ladder?" — No. e2e only. (User said so.)
- "Should each neuron be its own csproj/folder/slice?" — No. Slice = folder inside domain assembly. (User said so.)
- "Should we rename the old browser test base?" — Already decided. New name is `NeuronE2ETest<TNeuron>`.
- "Should we use IIncomingGrainCallFilter for the brain?" — Already implemented. The question is what's *next*, not whether.

### Prompts to engage with carefully

- "Should `Ino.Testing` ProjectRef IAW's `Testing` assembly?" — read `iaw/src/Testing/MockChatClient.cs` first.
- "Where exactly do screenshots go?" — `reviews/<TNeuron>/<test>.png` is my default; user may have a different preference.
- "Should `?semantics=1` be the default in tests so DOM assertions are possible?" — tradeoff: slower tests, but better render-correctness proof.
- "Do we keep `BrainStreamE2ETests` as a separate test, or fold it into `BrainNeuronE2ETests`?" — depends on whether brain itself is modeled as a neuron (it isn't today; it's plumbing).

---

## 9. House rules (re-statement)

From `CLAUDE.md`:

- ALWAYS Context7 for library APIs before writing code.
- NEVER read paths under `C:\Users\…`.
- NEVER use local NuGet cache.
- Always use latest NuGet versions.
- No `///` doc-comments. Self-explanatory variable names.
- Run code review before returning results.
- Tests must run with high severity. Aspire integration tests must be green.
- After making changes: `aspire do build`, `aspire run`, test.
- Use Aspire MCP tools (`mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")` etc.) for per-resource restart instead of full AppHost stop/start.

From conversation memory (decisions the user has confirmed elsewhere):

- Neuron behavior tests use Gherkin/Reqnroll. ino's e2e tests can also consume `.feature` files for the LLM mock corpus.
- 100 ms mock LLM delay is the default.
- No core architectural changes without asking the user first.
- UI layer owns client UX; response neurons stay platform-agnostic.
- Models stay hardcoded in framework, not configurable per-call.
- `WithReference(iaw)` does ALL env propagation — no extra calls.

---

## 10. Files to anchor on

| File | Why |
|---|---|
| `CLAUDE.md` | Project rules. Never violate. |
| `src/Ino.Testing/InoTestAppHost.cs` | Boots Aspire AppHost in tests. Inherit / extend. |
| `src/Ino.Testing.E2E/InoBrowserFixture.cs` | Adds Playwright. `NeuronE2ETest` builds on it. |
| `src/Ino.Core.Hosting/Llm/BddMockChatClient.cs` | Where 100 ms delay goes. |
| `src/Ino.Core.Hosting/Llm/BddScenarioLoader.cs` | How `.feature` files become scripted LLM responses. |
| `domains/travel/Ino.Domains.Travel.Tests/TripPlannerNeuronE2ETests.cs` | The target Plan Trip neuron test. First migration target. |
| `domains/travel/Ino.Domains.Travel.Tests/RichTripPlanningE2ETests.cs` | The 6-hop gRPC walk. Folds into the same migrated test. |
| `clients/ino.flutter/lib/screens/brain/brain_home_screen.dart` | Where the deep-link bug lives (§4.1). |
| `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs` | Read for the brain discussion (§6). |
| `iaw/src/Testing/AgentTest.cs` | IAW's test host; ino reuses through Aspire. |

End of file. Continue from §8.
