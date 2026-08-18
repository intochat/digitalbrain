# C2: The Brain and the Minimal Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land CoreV3's centerpiece and enforce the whitelist: the Brain (`Entity<BrainState>`: register · contexts · resolve · route · learn), entities re-based to plain `Grain` + `IPersistentState` on a new `Default` blob provider, the custom messaging pipeline/broadcast/graph/registry machinery deleted (plain grain calls + journals; `BroadcastChannel` for fan-out), module trims per the approved architecture page (Team/Orchestration, Introspection module, Button/Diagram die; `ChartNeuron` → `UIRenderer`; Surface → entity), and a final file-by-file sweep that deletes every remaining zero-consumer file. Every task keeps build `-warnaserror` + all three suites green.

**Architecture authority:** the approved CoreV3 page (artifact "DigitalBrain CoreV3") whose project trees are the WHITELIST — a file that maps to no tree entry and no consumer dies. Spec: `docs/superpowers/specs/2026-08-18-brain-core-refactor-design.md` (amended by this plan's Task 9: Brain-is-an-entity, ChartNeuron→UIRenderer, Registry absorbed now not deferred, entity persistence = `IPersistentState`).

**Fact base:** the 426-file inventory (2026-08-19) and this session's C1 reviews. Baseline: 20,527 src LOC; suites Aspire 16, Simulation 8/8, E2E 3/3 green at `18cef671`.

## Global Constraints

- `E:\intochat\digitalbrain`, branch `finalv2`. NEVER read or write any path under `C:\Users\`.
- **Gate per task:** `dotnet build DigitalBrain.slnx -warnaserror` exit 0, then all three suites green (foreground, no orphaned processes; kill + report >8 min): `tests/DigitalBrain.Aspire.Tests`, `tests/DigitalBrain.Simulation.Tests`, `tests/DigitalBrain.E2E.Tests` (Docker required). Timeout 600000 each.
- **Frozen contracts:** the chat wire (`UserMessaged → Pending → Running → Responded+Completed | Failed/Cancelled`, SSE `chat-delta`), the shell's auth endpoints, the facade's existing members, journal semantics (pinned by tests). Intentional pin changes are updated in the same task and NAMED.
- TDD where behavior is test-observable: Brain tests and renderer tests are written RED-first.
- Deletions via `git rm`; git history is the archive. Zero-consumer checks: `grep -rn "<TypeName>" --include="*.cs" src/ tests/ | grep -v obj` restricted to non-self hits.
- Commits per task, two `-m` flags, trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. No suppressions; no meaningless comments; self-explanatory naming (per the user's global rules) is part of the quality bar.

---

### Task 1: Minimal fabric delta — the `Default` grain-storage provider

**Files:** Modify `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs` (+Builder) — add `var grainState = storage.AddBlobs(DigitalBrainNames.GrainState);` and `.WithGrainStorage(DigitalBrainNames.DefaultGrainStorage, grainState)` on the AddOrleans chain; `RequireHealthyBeforeStart(grainState)`; expose `GrainState` on the builder. Modify `src/Kernel/DigitalBrain.Abstractions/DigitalBrainNames.cs` (+`GrainState = "grainstate"`, `DefaultGrainStorage = "Default"`). Modify `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs` — silo side: register the Azure blob grain storage named "Default" from the `grainstate` connection (mirror how DurableStateHosting reads its connection; use the Orleans Azure blob grain-storage package — check `Directory.Packages.props` for `Microsoft.Orleans.Persistence.AzureStorage`, already pinned). Modify `src/Testing/DigitalBrain.Testing/BrainSimulation.cs` — `silo.AddMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage)`.
**TDD:** Tier 1 fact first (kernel rendered env contains `ConnectionStrings__grainstate`) → RED → implement → GREEN. Gate. Commit: `"Add the Default grain-storage fabric for entities"`.

### Task 2: Entity re-base — plain grain, normal persistence

**Files:** Rewrite `src/Kernel/DigitalBrain.Core/Entities/Entity.cs`:
```csharp
public abstract class Entity<TState>(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TState> state)
    : Grain, IEntity<TState>, IOwnerBoundGrain
    where TState : class
{
    protected TState? State => state.RecordExists ? state.State : null;
    public Task<TState?> Read() => Task.FromResult(State);
    protected async Task SaveAsync(TState value, CancellationToken cancellationToken = default)
    { state.State = value; await state.WriteStateAsync(cancellationToken); }
}
```
(Verify `IPersistentState` member names — `RecordExists`/`WriteStateAsync(ct)` — against the pinned Orleans package; adapt and note.) The `DurableGrain` base, keyed `IDurableValue`, and `Serializer<TState>` uses go. Update the tests' `CounterEntity`/`ChartEntity` only if the base's protected surface shifted. **Gate note:** Simulation covers via memory Default; E2E exercises the real blob provider. All suites green. Commit: `"Re-base entities onto plain grains with Default persistence"`.

### Task 3: The Brain — register · contexts · resolve · route · learn

**Files:** Create `src/Kernel/DigitalBrain.Abstractions/Brain/` (`IBrain.cs` — `IBrain : IEntity<BrainState>` plus methods `Register(BrainReference)`, `Resolve(string hint, string? context)`, `UseContext(string)`, `Contexts()`, `Connect/Disconnect(Connection)`, `Route(string alias)`; `BrainState.cs` — nodes, connections, contexts (capped 32), activeContext; `BrainReference.cs` (Kind: Neuron|Entity, Type, Name, LastUsed), `BrainContext.cs`, `Connection.cs`, `RouteOutcome` MOVED from Graph/). Create `src/Kernel/DigitalBrain.Core/Brain/BrainEntity.cs` (`[GrainType("brain")]`, the implementation: resolution = context-scoped name/type/recency match with per-context usage tallies; routing = connections first, `CapabilityIndex` second). Modify `Core/Neuron/Neuron.cs` — on activation, fire-and-forget `Register` to the owner's brain (direct grain call; must not block activation). Modify `src/Kernel/DigitalBrain.Client/` — facade additions (`ActiveContextAsync`, `ContextsAsync`, `UseContextAsync`, `ResolveAsync`).
**TDD:** `tests/DigitalBrain.Simulation.Tests/BrainTests.cs` FIRST (RED): activation registers the neuron; `GetEntity` use registers the entity (via facade instrumentation or renderer — choose the honest trigger and document); `ResolveAsync("chart")` finds the chart touched in the active context; `UseContextAsync` switches; repeated use biases resolution (two charts, the used-in-context one wins). Then implement → GREEN. Gate. Commit: `"The Brain: registry, contexts, resolution, routing, learning"`.

### Task 4: Pipeline removal — a send is a grain call

**Files:** Delete `Core/Neuron/{NeuronMessagePipeline,NeuronOutbox,NeuronTurnCoordinator,NeuronDeliveryMemory,NeuronStreamRegistry,NeuronCapabilityCoordinator,ConnectionRelayNeuron}.cs`, `Core/Outbox/` (4), root `{BroadcastCatalog,BroadcastRoute,BroadcastTopology,DeliveryPolicy,DeclarativeSynapseTransform,ISynapseTransform}.cs`, `Core/Hosting/{AssemblyBroadcastHandlers,IConfigureBroadcastCatalog}.cs`, `Abstractions/Messaging/BroadcastAttribute.cs`, `Core/Serialization/{DispatchManifest,SynapseDispatch,SynapseWiring,SynapseWiringEntry}.cs` (verify each is pipeline-only first — zero-consumer check; any survivor consumer gets a minimal port). Rewrite `Neuron.SendAsync`/`EmitAsync`: journal-stage Outgoing → direct `GrainFactory` call to the target's `IHandle<T>` dispatch (resolve target via the Brain's `Route` when unaddressed) → target journals Incoming in its filter/base. Fan-out (`DigitalBrainActivated` → SurfaceBoot etc.) moves to Orleans **BroadcastChannel** (register in `DigitalBrainRuntime`; consult Context7 for the 10.2 API before writing). `SystemTools.FireCoreAsync`'s dispatch keeps working (it already resolves + calls; it loses outbox polling in favor of awaiting the reply through the session journal as the client does — read it and keep the smaller change).
**The frozen chat contract is the canary:** ChatTurnTests + E2E feature must stay green with the pipeline gone. Delivery semantics change from at-least-once-outbox to direct-call — the plan accepts this (pre-prod ruling; journals still record everything). Gate. Commit: `"A send is a grain call: delete the outbox, pipeline, and broadcast machinery"`.

### Task 5: Graph + Registry absorbed by the Brain

**Files:** Delete `Core/Neuron/{SynapseGraphNeuron,DigitalBrainNeuron}.cs`, `Core/Registry/` (2), `Abstractions/Registry/` (14), `Abstractions/Graph/` (rest — `RouteOutcome` already moved in T3), `Abstractions/Neurons/IDigitalBrainNeuron.cs`. Migrate: `ChatTurnWorker.ResponderAsync`'s `ConnectionsFrom` → `brain.Connections(chatId, ChatRoles.Responder)`; `SystemTools.GetNeuronsAsync` → Brain registry read; `DigitalBrainClient.ActivateAsync`'s `Brain().Activate()` → the BrainEntity's register/activate path (facade `ActivateAsync` now touches the Brain + session neuron — keep the activation journal footprint the Tier 2 smoke pins, or update that pin NAMED); kernel `MapBrainTopology`/`MapGraphStreams` → read/stream from the Brain (`MapGraphStreams.cs` renamed `MapBrainStreams.cs`; topology DTOs `BrainNeuron/BrainConnection/BrainModule/BrainTopologySnapshot/GraphEvent` re-pointed or slimmed — audit each, delete orphans). `db.connect`/`Connect` synapses become Brain method calls surfaced via a `brain_connect` MCP tool + assistant tool (update `SystemTools`/Assistant prompt minimally). Gate. Commit: `"The Brain absorbs the graph and the registry"`.

### Task 6: UI module to the target shape — UIRenderer, chart entity, surface entity

**Files:** Create `Modules/UI/Contracts/Render/IUIRenderer.cs` (+ synapses: it accepts `ChartPoint` and `OpenSurface`-class requests) and `Modules/UI/.../Render/UIRenderer.cs` (`[GrainType("uirenderer")]`, grants-checked writes into entities). Transform `IChart` → `IChart : IEntity<ChartState>` (merge `IChartEntity` into it; `[Alias("ui.chart")]` kept, `[GrainType("chart")]` on `ChartEntity` — the grain type the corpus fires at stays `chart:demo`; ChartCard handler reads `GetGrain<IChart>` now). Delete `ChartNeuron.cs`, `IChartEntity.cs`. Transform Surface: `ISurface : IEntity<SurfaceState>` + `SurfaceEntity`; `SurfaceBoot`/`Surface` neuron audit — keep a slim boot neuron ONLY if the activation flow still needs a receiver (read the T5 outcome), else delete. Delete `Button/` (contracts+impl) and `Diagram/` (contracts+impl) — zero-consumer verify vs the Flutter shell wire first (ChatButtonOffer stays in Chat contracts — `Responded.Buttons` is frozen wire; only the Button NEURON dies). Update `UiModule.cs` registrations; update Chat's ChartCard handler + corpus flow; migrate grants: write-path checks live in `UIRenderer`.
**TDD:** renderer tests RED-first in `tests/DigitalBrain.Simulation.Tests` (fire `ui.chart-point` → renderer routes → chart entity holds it → grants refusal path). The MVP corpus keeps working (`chart:demo` target unchanged). Gate (ChatTurnTests + e2e feature green). Commit: `"UIRenderer writes entities; the chart is a pure entity"`.

### Task 7: Module trims — Orchestration, Introspection module, AI/whisper orphans

**Files:** Delete `Modules/AI/AI/Orchestration/` (13) + `Modules/AI/Contracts/{IGroupChat,ITeam,TeamFormation,OrchestrationRefusedException}.cs` + `Assistant.ConveneAsync` (and its prompt mention). Delete `Modules/Introspection/` entirely (both projects, slnx, ProductModules, csproj refs); rewrite `Mcp/IntrospectionTools.cs` thin over the facade (`ReadJournalAsync` + tallies from `JournalRead`/`JournalSnapshot`) keeping tool names; delete the Mcp DTOs that depended on module contracts, keep `NeuronJournalPage`/`JournaledSynapse` shapes local. Retire the `DigitalBrain.Sdk` project: move `Protection/` (3 files) to `Core/Security/`, update the two consumer references (AIModule, config), remove project from slnx. Delete `Aspire/DigitalBrain.Aspire/DigitalBrainScriptHost.cs`. Zero-consumer audits with delete-on-zero: `StreamingUsageChatClientExtensions`, `IWhisperSmall`, `IWhisperTiny`, `ConstantParameterDefault`, `OperatorSuppliedParameterDefault`, `Abstractions/Integrations/` (2), `Messaging/Provenance`, `Identity/ModuleId`, `Core/Identity/{PrincipalGrants,PrincipalGraph}`, `ReminderSourceAllowlist`, `Mcp/ActiveNeuron`, capability lifecycle synapses (`CapabilityAbandoned/Completed/Failed/Rejected/Requested`), `Core/Capabilities/{CapabilityOutcome,CapabilityRequestContext}` — each: grep, delete on zero non-self consumers, name every verdict in the report. Gate. Commit: `"Trim modules to the whitelist; retire the Sdk project"`.

### Task 8: The sweep — every remaining file justifies itself

**Files:** none prescribed — this task PRODUCES the final deletion list. Procedure: list every `src/**/*.cs`; for each file not named in the CoreV3 trees (the artifact page's tabs, reproduced in the task report for the record): zero-consumer check → delete on zero; a consumed file that maps to no tree entry gets a KEEP-WITH-REASON line in the report (these become tree amendments in Task 9, or controller-ruled deletions). Also: per-project `GlobalUsings.Abstractions.cs` — regenerate to only namespaces that still exist. Gate. Report the final LOC (`find src -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | xargs cat | wc -l` — compute per-batch totals correctly, the C1 counting bug is known). Commit: `"Whitelist sweep: every file earns its place"`.

### Task 9: Quality pass + spec/tree sync + full gates

- Quality pass over the KEPT core files (Neuron, Entity, BrainEntity, Chat, UIRenderer, filters, facade): naming self-explanatory, no stale comments, no dead usings — changes only where a reader stumbles, no churn.
- Spec amendments: Brain-is-an-entity; ChartNeuron→UIRenderer; Registry absorbed in C2; entity persistence `IPersistentState`; the C2 outcome LOC. Update `docs/JOURNALS.md` if wording drifted.
- Full gates recorded (three suites + counts + durations + final LOC vs 20,527 and vs the original 32,127).
- Commit: `"C2 complete: spec and docs sync"`.
