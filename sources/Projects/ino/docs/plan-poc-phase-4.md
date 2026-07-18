# ino POC Phase 4 — IAW capability bridge + ML self-improvement + L1 loop

Companion to [`product-vision-final.md`](./product-vision-final.md) and successor to [`plan-poc-phase-3.md`](./plan-poc-phase-3.md). Sequenced, testable, concrete.

## Mission

Phase 3 landed the multi-hop primitive (legacy plan grain + `TraversalEngine` + `RecallQuery`). Phase 4 does three things on top of it:

1. **Close the open-closed Cortex gap** (issue [#24](https://github.com/LeftTwixWand/ino/issues/24)) so kernel code stops being edited every time a domain ships a new synapse.
2. **Lift IAW capabilities into ino domains** — Reminders (scheduling) and Recall (semantic memory) become first-class domains routed through Cortex like Travel/Taxi. `LlmNeuron<TEvent>` already inherits `IAW.Core.Agent`, so the runtime is in place; what's missing is the domain routing surface and per-user keying.
3. **Ship the first self-improvement loop** — NeuronML for ino's own decisions (Slice D), then CreatorNeuron + L1 activation (Slice E, issue [#25](https://github.com/LeftTwixWand/ino/issues/25)).

Each slice is shippable independently. Slices A → E in order; A unblocks the cleaner plan dispatch path used by every following slice.

## Approach — thin vertical slices

Same rule as Phase 3: each slice cuts neuron(s) + synapse(s) + domain routing + BDD + per-domain test project. No layer is built alone. Per-slice success criteria from `CLAUDE.md`'s verification loop:

1. `dotnet build ino.slnx`
2. `dotnet test ino.slnx`
3. `aspire run` (or `aspire start --isolated`) — every resource Healthy in the dashboard
4. Drive the scenario in Chrome via the kernel-silo HTTPS URL; confirm Aspire Structured Logs (filter `ino-flutter`) and Traces (`grpc Chat`, `fire`/`handle` spans linked by `traceparent`)
5. E2E (`dotnet test test/Ino.E2E.Tests`) and the per-domain test project green
6. Iterate via `mcp__aspire__execute_resource_command(resourceName="…", commandName="rebuild")`

Context7 mandatory before writing library-touching code (Orleans 10 DurableJobs, ML.NET / LightGBM, `Microsoft.CodeAnalysis.CSharp.Scripting`, Qdrant, Aspire 13). Listed per slice below.

Commit discipline: granular `feat(poc):` / `test(poc):` / `fix(poc):` / `refactor(poc):` / `docs:`, one logical change per commit. Autopilot from Phase 3 still applies — commit green → push → next slice.

---

## Milestone A — Open-closed Cortex (slice A)

### Slice A — Convert five legacy single-hop routes to plan grains, delete the switch

**Why first.** Every slice below adds a domain route. Today every new route either gets a plan grain (Phase 3 Slice A path) or hits the hardcoded switch in `src/Ino.Kernel/CortexNeuron.cs:214`. Closing #24 means slices B–E only need the plan path — no kernel edit per domain.

**Build.**
- For each of the five legacy routes — `travel.find-flights`, `travel.find-hotels`, `travel.find-places`, `travel.plan-trip`, `taxi.find-ride` — add:
  - `IXxxPlan` in `<domain>.Contracts` (e.g. `Ino.Domains.Travel.Contracts/IFindFlightsPlan.cs`).
  - `XxxPlan : Grain, IXxxPlan` in `<domain>` (e.g. `domains/travel/Ino.Domains.Travel/Plans/FindFlightsPlan.cs`). The plan body is one-hop: extract args from the prompt (for v0.1 just pass the prompt through, same as the switch does today) and `engine.FireAsync(new XxxRequest(...), ct)`.
  - Wire the plan type on the domain route declaration in `Travel.cs` / `Taxi.cs`.
- Once all five routes have plans:
  - Delete the `switch` block at `CortexNeuron.cs:214–231`.
  - Simplify the routability check to "plan type is not null" (the early-return on line 276 is already there; drop the trailing type list).
  - Drop the now-unused `using Ino.Domains.Taxi.Contracts;` / `using Ino.Domains.Travel.Contracts;` from `CortexNeuron.cs` and the corresponding ProjectReferences from `Ino.Kernel.csproj` (kernel no longer needs to know about per-domain synapse shapes).
- Tests:
  - Update `CortexNeuronTests` cases that captured payloads via the switch to assert the right plan grain was invoked instead (NSubstitute `grainFactory.Received(1).GetGrain(typeof(IFindFlightsPlan), key)`).
  - One BDD scenario per #24 acceptance: install a fixture domain from `domains/testing/`, fire its route via natural language, assert routing works without kernel edits.

**Test.**
- `test/Ino.Kernel.Tests/CortexPlanDispatchTests.cs` — extend with assertions that the kernel project no longer references Travel.Contracts / Taxi.Contracts.
- New BDD scenario in `domains/testing/Ino.Testing.Fixture.Alpha/Features/alpha-intent.feature` registering a synthetic route and asserting Cortex routes to its plan after `mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")` — no other kernel edit.

**Context7.** Orleans 10 grain interface resolution, plain `IGrainFactory.GetGrain(Type, string)` overload semantics under multi-host clustering (already verified in Phase 3 Slice A for plan dispatch — re-verify if Slice A bumps Orleans).

**Done when.**
- `Ino.Kernel.csproj` no longer references `Ino.Domains.Travel.Contracts` or `Ino.Domains.Taxi.Contracts`.
- The fixture domain BDD scenario is green without any kernel edit.
- All five legacy routes route via plans; existing E2E tests continue to pass.

**Closes.** [#24](https://github.com/LeftTwixWand/ino/issues/24).

---

## Milestone B — IAW capability bridge (slices B + C)

The premise: `LlmNeuron<TEvent>` inherits `IAW.Core.Agent`, so every ino LLM-backed neuron already has scheduling (`Agent.Scheduling.cs`), durable chat history (`Agent.State.cs`), tool registration (`Agent.Tools.cs`), and the option to mount a memory context provider. What's missing is the **domain route surface** — phrases like "remind me to call mom at 6 pm" need to land on a neuron that calls into those substrates rather than each domain reimplementing them.

These two slices establish the "domain route as thin wrapper over IAW substrate" pattern that future domains will follow.

### Slice B — Reminders domain (scheduling route)

**Goal.** "remind me to call mom in 30 minutes" → Cortex routes to `reminders.set` → `SetReminderPlan` calls `Agent.ScheduleJob` on a `RemindersNeuron : LlmNeuron<ReminderEvent>` keyed by user → at due time the neuron fires `ReminderDue` synapse → kernel narrates back to the user via the gateway.

**IAW surface this slice consumes** (already in tree):
- `iaw/src/Core/Agents/Agent.Scheduling.cs` — `ScheduleJob(name, delay, prompt, ct)` / `ScheduleRecurringJob(name, interval, prompt, ct)` / `CancelJob(name, ct)` / `ListJobs(ct)`. State is persisted to `durableState.ScheduledJobs : IDurableDictionary<string, ScheduledJobItem>`. Backed by Orleans `DurableJobs` (the 10.1 API rename note in `CLAUDE.md` is already applied).
- `OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)` — virtual override the neuron uses to convert the scheduled prompt into a synapse fire.

**Build.**
- New domain at `domains/reminders/`:
  - `Ino.Domains.Reminders.Contracts/`
    - `Reminders.cs` — `[GenerateSerializer] public sealed class Reminders : IDomain { ... DeclaredRoutes = new[] { reminders.set, reminders.list, reminders.cancel } ... }`.
    - `ReminderSet.cs`, `ReminderDue.cs`, `ReminderCancelled.cs` — synapses (records implementing `ISynapse`).
    - `ISetReminderPlan`, `IListRemindersPlan`, `ICancelReminderPlan`.
    - `IRemindersNeuron : IGrainWithStringKey, IJournaledNeuronQuery<ReminderEvent>` with explicit user-keyed methods `SetAsync(string description, TimeSpan delay, string correlationId)` / `CancelAsync(string name, ...)` / `ListAsync()`. Same shape as `ILocationNeuron` from Phase 3 — bypasses correlation-keyed FirePort because reminders are stateful per-user.
  - `Ino.Domains.Reminders/`
    - `Neurons/RemindersNeuron.cs` : `LlmNeuron<ReminderEvent>, IRemindersNeuron`. Ctor inherits IAW's `[AgentState] AgentDurableState durableState, IChatClient chatClient` plus the keyed journal. `SetAsync` calls `await ScheduleJob(name, delay, prompt, ct)` (inherited) then `RaiseAsync(new ReminderSet(name, prompt, dueAt), ctx, ct)` to write the journal entry.
    - **Override** `OnScheduledJobDueAsync` to:
      1. Build a `NeuronContext` with the user id reconstructed from the grain key.
      2. `RaiseAsync(new ReminderDue(job.Name, job.Prompt), ctx, ct)`.
      3. `firePort.FireBroadcast(new ReminderNarration(job.Prompt, userId), ctx, ct)` so the gateway streams the reminder text back to the user. (`ReminderNarration` is a synapse the gateway subscribes to today via `ChatStream`; verify the subscription exists or extend it.)
    - `Plans/SetReminderPlan.cs` — same static-body pattern as `OrderRideHomePlan`. Uses the LLM only to extract `(description, delay)` from the prompt; calls `IRemindersNeuron.SetAsync` directly. Returns `NeuronResult.Ok($"OK, I'll remind you in {delay.Humanize()}.")`.
    - `Plans/ListRemindersPlan.cs` and `Plans/CancelReminderPlan.cs` — symmetric.
    - `Reminders.cs` — `IDomain` marker. Three domain routes with `PlanType` set; no declared routes for substrate-only neuron access (same shape as Location).
    - `Program.cs` — silo entrypoint, ports `11117 / 30006` (next free pair after Location).
    - `Features/reminders-intent.feature` — `@neuron:reminders.set Scenario: Remind me later` etc., regex like `remind me|set a reminder|in (\d+) (min|minutes|hour|hours)`.
- AppHost wiring: `builder.AddProject<Projects.Ino_Domains_Reminders>("reminders").PropagateInoConfig(ino);` in `src/Ino.AppHost/Program.cs`.
- `ino.slnx` — add the three projects under `/domains/reminders/`.
- `Ino.Kernel.csproj` — add `Ino.Domains.Reminders.Contracts` ProjectReference (for the `ReminderNarration` subscription path on the gateway) but NOT the impl project.

**Tests.**
- `domains/reminders/Ino.Domains.Reminders.Tests/`:
  - `RemindersNeuronTests` — 4 tests: set persists to journal + ScheduledJobs; OnScheduledJobDueAsync fires both `ReminderDue` and `ReminderNarration`; cancel removes the durable job and journals `ReminderCancelled`; list returns the live `ScheduledJobs` snapshot. Use `InoTestSiloFixture` like Location tests; mock `IFirePort`.
  - `SetReminderPlanTests` — 3 tests on the plan static body: extracts (description, delay) under a mock `IChatClient` configured with `BddMockChatClient` against the `.feature` corpus; missing time → friendly clarification (no IRemindersNeuron call); valid input → `SetAsync` invoked once.
- `test/Ino.Kernel.Tests/CortexPlanDispatchTests` — extend with `reminders.set` regex hit assertion.
- E2E (`test/Ino.E2E.Tests`) — full loop: chat "remind me to test ino in 1 minute" → wait → assert `ReminderNarration` arrives on the gateway stream within `delay + 30s`.

**Context7.** Orleans 10 `DurableJobs` (the `IJobRunContext` / `ScheduleJobRequest` shape per the rename note in `CLAUDE.md`). Humanizer (already pinned via central package management).

**Done when.**
- "remind me to call mom in 1 minute" via the Flutter chat surface produces a `ReminderNarration` ~1 minute later, reading "calling mom" or similar.
- Aspire dashboard shows the OTel span chain `grpc Chat` → `fire ReminderSet` → (gap) → `fire ReminderDue` → `fire ReminderNarration`, all sharing a `correlationId`.
- Killing and re-running the AppHost between SetAsync and the due time still fires the reminder (`Agent.RescheduleExistingJobsAsync` is invoked on activation — already implemented in IAW). This validates that IAW's durable scheduling actually survives a restart in the ino topology, even before #22 lands real persistence.

**Out of scope for the slice.** Recurring reminders (the IAW API supports them; expose `reminders.recurring` as a follow-up). Cross-domain reactor (e.g. travel auto-creates "leave for airport in 2h" reminder) — that's Phase-4 epilogue, not gating.

---

### Slice C — Recall domain (semantic memory route)

**Goal.** "what did I tell you about my mum's birthday?" → Cortex routes to `recall.search` → `RecallPlan` calls into `IawMemoryProvider.LookupOriginAsync` → narrates the hit back. And: every chat turn through the gateway is auto-stored to that user's Qdrant collection, so recall has something to find.

**IAW surface this slice consumes** (already in tree):
- `iaw/src/Core/Memory/IawMemoryProvider.cs` — `MessageAIContextProvider` + `IMemoryLookup`. Per-user collection `user-memory-{userId}`, embedding via injected `IEmbeddingGenerator<string, Embedding<float>>`, stored to Qdrant with payload keys `content` / `userId` / `role` / `createdAtTicks` / `threadId` / `sourceTelegramMsgId`. Recall: `LookupOriginAsync(userId, question, ct)` returns the top-1 `MemoryHit`.
- The Aspire substrate's Qdrant resource (declared in `iaw/src/Aspire.Hosting`) is already wired through `AddIno() → AddIAW()`; ino silos already have `QdrantClient` available via `ino.Iaw` reference.

**Build.**
- New domain at `domains/recall/`:
  - `Ino.Domains.Recall.Contracts/`
    - `Recall.cs : IDomain`. One route: `recall.search`.
    - `RecallQuestion`, `RecallAnswer` synapses.
    - `IRecallPlan`.
    - `IRecallNeuron : IGrainWithStringKey` with `LookupAsync(string question, CancellationToken ct)`.
  - `Ino.Domains.Recall/`
    - `Neurons/RecallNeuron.cs : Grain, IRecallNeuron` (pure-code, no LLM, no journal — semantic memory IS the journal here, just lives in Qdrant). Ctor `(IMemoryLookup lookup)`. `LookupAsync` extracts the user id from the grain key and calls `lookup.LookupOriginAsync(userId, question, ct)`.
    - `Plans/RecallPlan.cs` — extract the question text from the prompt (often the prompt verbatim minus the verb), call `IRecallNeuron.LookupAsync`, narrate the hit. Same static-body pattern as Phase 3 plans.
    - `Recall.cs` marker, `Program.cs` silo entry on ports `11118 / 30007`.
    - `Features/recall-intent.feature` — regex like `what did I (say|tell you)|recall|do you remember`.
- New auto-store hook in the kernel:
  - `src/Ino.Kernel/MemoryAutoStoreObserver.cs` — `IChatStreamObserver` (or extend an existing observer) that on every `ChatIntent` + final `ChatStream.Send` writes both messages to `IawMemoryProvider.StoreAIContextAsync` for the user. The IAW provider's existing `StoreAIContextAsync` runs from `MessageAIContextProvider.InvokedContext`; for ino we call it directly with a synthetic context built from the gateway turn.
  - Reading `IAW.Core.Memory.IawMemoryProvider.ReadUserId()` uses `AIAgent.CurrentRunContext?.Session?.StateBag` — that ambient is set by IAW's chat pipeline. ino's gateway needs to push `iaw.userId` onto the same bag before invoking the provider, OR — cleaner — the kernel observer calls Qdrant directly via `QdrantClient` for the ino path. Choose by trace inspection: if the IAW ambient is already populated (because `LlmNeuron` sets it via `Agent` activation), reuse the provider; otherwise use the direct Qdrant path. Verify before coding.

**Tests.**
- `domains/recall/Ino.Domains.Recall.Tests/RecallNeuronTests` — 3 tests against a real Qdrant test container (Aspire test fixture pattern from `Ino.Testing.E2E`): seed three messages, search, assert top-1 hit content. Skip when the test container can't start (CI flag).
- `RecallPlanTests` — 3 tests on the plan body: missing user → friendly clarification; hit found → narration includes `MemoryHit.CreatedAt` formatted; no hit → "I don't recall that yet."
- E2E — full Flutter loop: chat "my mum's birthday is March 12" → 30 s → chat "when is my mum's birthday?" → assert response contains `March 12`.

**Context7.** Qdrant.Client (collection lifecycle, per-user collections vs single-collection-with-filter — this slice keeps IAW's per-user shape). `Microsoft.Extensions.AI` `IEmbeddingGenerator<string, Embedding<float>>` provider selection (xAI does not currently expose embeddings; this slice depends on either a Foundry Local embedding model already wired by IAW or adding one to `Ino.Llm.<provider>`).

**Done when.**
- "my favourite colour is purple" → 30 s later "what's my favourite colour?" → ino answers "purple".
- Qdrant dashboard shows the user-keyed collection populated.
- E2E green; per-domain tests green.

**Out of scope for the slice.** Memory decay (issue [#23](https://github.com/LeftTwixWand/ino/issues/23) — independent track). Cross-thread / cross-domain memory (single user collection is enough for v0.1). Memory reinforcement on read.

---

## Milestone C — ML self-improvement (slice D)

### Slice D — NeuronML on Cortex itself

**Goal.** Stop paying for the LLM classifier on prompts the regex fast-path or a learned router can answer. CortexNeuron records every routing decision (regex hit, LLM classifier hit, unrouted) as a `DecisionRecord`; after 50 records a per-user `NeuronOptimizer` LightGBM grain trains; subsequent prompts hit the model first and only fall through to the LLM classifier on low confidence.

The design is already specced in [`docs/neuron-ml.md`](./neuron-ml.md). What's new is **applying it to Cortex first**, instead of (or in addition to) the IAW Approver. Cortex is a much louder source of decisions than authorization, so the loop closes faster and the savings are more visible.

**Build.**
- Port from `iaw/src/Core/ML/` (or wherever NeuronML actually lives in iaw — verify via Glob before coding) into `src/Ino.Core.Hosting/ML/`:
  - `FeatureSchema.cs`, `FeatureCatalog.cs`, `DecisionRecord.cs`, `OptimizationResult.cs`, `INeuronOptimizer.cs`, `NeuronOptimizerGrain.cs`, `NeuronOptimizerState.cs`, `IFeatureArchitect.cs`, `FeatureArchitectGrain.cs`. Keep the namespaces under `Ino.Core.Hosting.ML` so ino-only domains depend only on `Ino.Core.Hosting`, not on IAW's ML namespace.
  - Verify the existing files actually exist before declaring this a port; if iaw doesn't currently have them in the tree (`docs/neuron-ml.md` references future-tense paths), build them fresh against the spec. **Run `Glob iaw/src/Core/**/ML/**` first.**
- Central package pins (in root `Directory.Packages.props`):
  - `Microsoft.ML` (5.0.0-preview.1)
  - `Microsoft.ML.LightGbm` (5.0.0-preview.1)
- Plug Cortex in:
  - In `CortexNeuron.HandleAsync`, after fast-path / classifier resolves, build a `DecisionContext`:
    - features from the catalog: `ToolNameHash` → route id hash; `CallerHash` → user id hash; `ArgsComplexity` → prompt length / token count; `ContextLength` → corpus size; `TimeOfDay` / `DayOfWeek`; `HistoricalSuccessRate` from previous routing outcomes for this user.
    - label: 1 if routed (fast-path or classifier), 0 if unrouted.
  - Per-user `INeuronOptimizer` grain: `var opt = grainFactory.GetGrain<INeuronOptimizer>($"cortex-{userId}");`
  - Before classification: `var pred = await opt.Predict(features); if (pred is { Confidence: >= 0.90 }) return RouteByPredictedRouteId(pred);`
  - After every decision: `await opt.Record(new DecisionRecord(features, label));`
- `FeatureArchitectGrain` — bootstrap one schema per neuron type (`cortex-router`, `reminders-extractor`, etc.) on first activation. Schema is durable; only retrains schema if the catalog grows.
- Inspector (Flutter) ML panel — read from `IInoGateway.GetMlStateAsync(GrainId)` (per Phase 3 Slice 4 design); show per-user counters `ino.ml.predictions` / `.fallbacks` / `.retrains`.

**Tests.**
- `test/Ino.Core.Hosting.Tests/NeuronOptimizerTests` — port the 5 BDD scenarios from `docs/neuron-ml.md`'s "BDD tests" section (records and trains after threshold; predicts with high confidence; returns null before training; FeatureArchitect designs schema; NeuronCreator births neuron with ML — drop the last one until Slice E).
- `test/Ino.Kernel.Tests/CortexMLRoutingTests` — 3 tests: 60-record warmup with two clear patterns then assert subsequent matching prompts return without invoking `IChatClient`; assert OTel counters increment.
- E2E — drive the gateway with 100 turns of synthetic prompts, then a 101st identical-shape prompt, and assert via Aspire trace inspection that no `gen_ai.*` span fires for that final prompt.

**Context7.** ML.NET 5.0 preview API surface, LightGBM binary classifier, `mlContext.Model.ConvertToOnnx` for the future GPU path.

**Done when.**
- After 50+ "find flights to Bali" prompts, the 51st routes without an `IChatClient` call (visible in Aspire traces — no `gen_ai.*` span for that prompt).
- `ino.ml.retrains` counter visible in dashboard, increments at decision 50, 75, 100…
- Inspector ML panel shows confidence histogram for `cortex-router`.

**Out of scope for the slice.** Mandelbrot multifractal analysis (Phase 3 Slice 3 / `docs/neuron-ml.md` future work). GPU prediction path — CPU LightGBM is microseconds, ship CPU first. Per-neuron optimizers below Cortex (e.g. `find-flights` ranker) — follow-up slice.

---

## Milestone D — L1 self-improvement loop (slice E)

### Slice E — CreatorNeuron + close #25

**Goal.** Three identical-shape unrouted prompts → cluster crosses threshold → `MissedIntentAggregator` emits an `L1Proposal` → user approves in the inspector → `CreatorNeuron` Roslyn-compiles a new neuron route + (sometimes) a new synapse contract → registers it via the dynamic neuron registry → next prompt of the same shape routes successfully **without a silo restart**.

This slice depends on Slice A (plan dispatch is the only routing path for new routes), benefits from Slice C (recall is the natural store for "example prompts that landed in the cluster"), and demonstrates Slice D's value (the new neuron starts learning from decision 1).

**Build.**

#### Step E.1 — `MissedIntentAggregator` neuron in the kernel
- `src/Ino.Kernel/MissedIntentAggregator.cs` — `Neuron<UnroutedIntent>` (pure-code, no LLM). Subscribes to broadcasted `UnroutedIntent` synapses (already emitted at `CortexNeuron.cs:317`).
- Embedding-based clustering using the same `IEmbeddingGenerator` from Slice C. Window: last 24 h. Threshold: 3 near-duplicates (cosine similarity > 0.85).
- When a cluster crosses threshold: `firePort.FireBroadcast(new L1Proposal(...), ctx, ct)`.
- `L1Proposal` synapse — defined in `Ino.Kernel.Contracts` so Genesis/CreatorNeuron can react cross-silo:
  - `string ProposedRouteId` (e.g. `reminders.recurring` if the cluster looks like recurring-reminder asks)
  - `string DraftSynapseShape` (record name + field list, drafted by an LLM)
  - `IReadOnlyList<string> ExamplePrompts`
  - `string Rationale` (LLM-generated)
  - `string ProposalId` (ulid)

#### Step E.2 — `Ino.Domains.Genesis` with `CreatorNeuron`
- New domain at `domains/genesis/`:
  - `Ino.Domains.Genesis.Contracts/`
    - `Genesis.cs : IDomain`. One route: `genesis.create-from-proposal` (system route — not surfaced to end users).
    - `NeuronCreated`, `NeuronActivationFailed` synapses.
    - `ICreatorNeuron : IGrainWithStringKey` — `Task<NeuronResult> CreateFromProposalAsync(L1Proposal proposal, CancellationToken ct)`.
  - `Ino.Domains.Genesis/`
    - `Neurons/CreatorNeuron.cs : LlmNeuron<NeuronCreated>, ICreatorNeuron`. The "first ino neuron" of the user's vision: it creates other neurons. `[PinToSilo("genesis")]` — singleton-ish like Cortex, since registry mutations need to serialize.
    - `Compilation/PlanCompiler.cs` — wraps `Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript`. Inputs: `L1Proposal` + a fixed Roslyn script template:
      ```csharp
      // Template (string interpolated with proposal fields):
      using Ino.Core; using Ino.Core.Hosting;
      public sealed record {SynapseName}({Fields}) : ISynapse;
      public interface I{PlanName} { }
      public sealed class {PlanName}(IFirePort fp, IGrainFactory gf)
          : Grain, I{PlanName}
      {{
          public async Task<NeuronResult> ExecuteAsync(NeuronRouteContext input, CancellationToken ct)
          {{
              {GeneratedBody}
              return NeuronResult.Ok();
          }}
      }}
      ```
      Reuse `iaw/src/Agents.CSharp/Roslyn/RoslynAgent.cs` as the script-eval entrypoint; do NOT introduce a second compiler integration. Verify the API surface there before wiring.
    - `Registry/INeuronRouteRegistry.cs` + in-memory impl. Behind an interface so #22 can drop in a durable-per-user implementation later. The registry holds compiled `Assembly` references + route metadata; `Discovery` queries it on every `DumpNeuronRoutesAsync` call.
    - `Plans/CreateFromProposalPlan.cs` — bridges Cortex (when an admin/inspector approves a proposal via `genesis.create-from-proposal`) to `ICreatorNeuron.CreateFromProposalAsync`.
    - `Genesis.cs`, `Program.cs` ports `11119 / 30008`.

#### Step E.3 — Wire registry into Discovery
- `src/Ino.Core.Hosting/Discovery.cs` (or wherever `DumpNeuronRoutesAsync` lives — verify) — concatenate static domain routes with `INeuronRouteRegistry.GetDynamicRoutes()`. Caches keyed by registry version. Activation:
  - On `CreatorNeuron.CreateFromProposalAsync` success → bump registry version → invalidate the discovery cache → next `DumpNeuronRoutesAsync` returns the new route.
- No silo restart.

#### Step E.4 — Inspector "Proposals" pane
- Flutter: new tab in the inspector drawer (Decision 12 of the vision). Lists pending `L1Proposal`s with example prompts + draft spec + Approve / Reject buttons.
- Approve → fires `genesis.create-from-proposal` with the proposal. Reject → `firePort.FireBroadcast(new ProposalRejected(proposalId), ctx, ct)` so the aggregator stops re-emitting the same cluster.

**Tests.**
- `domains/genesis/Ino.Domains.Genesis.Tests/`
  - `PlanCompilerTests` — 3 tests: well-formed proposal compiles; malformed body returns compilation errors as `NeuronActivationFailed`; compiled assembly hosts the right plan alias. Use the test silo + a real `RoslynAgent`.
  - `CreatorNeuronTests` — 3 tests: registry version bumps; Discovery returns the new route on the next call; second identical proposal is rejected (no double-create).
- `test/Ino.Kernel.Tests/L1LoopTests` — full BDD per #25 acceptance: 3 identical-shape unrouted prompts → assert `L1Proposal` emitted; auto-approve via test fixture; 4th prompt with the same shape routes successfully without restarting any silo. **This is the gating test for the slice.**
- E2E — same scenario but driven via Flutter chat + the Proposals pane, with a `git clean -fdx` between the 3 unrouted prompts and the 4th, just to prove the in-memory registry survives a hot iteration cycle. (It won't survive a true cold boot until #22; that's expected and called out as a known gap.)

**Context7.** `Microsoft.CodeAnalysis.CSharp.Scripting` (CSharpScript options, references, globals). Orleans 10 dynamic grain registration — verify whether plan grains compiled at runtime can be resolved by `IGrainFactory.GetGrain(Type, string)` without pre-registration in the silo manifest. If not, the activation path needs a thin hosted route wrapper grain that takes the compiled assembly + plan type as constructor parameters and dispatches to the loaded body. **This is the highest-risk Context7 question of Phase 4.**

**Done when.**
- The acceptance demo passes: install a stripped-down silo (no Reminders), prompt 3× with "remind me to call mom" → `L1Proposal` emitted → approve via inspector → 4th prompt routes to a freshly-created `reminders.set` plan and the user gets a reminder.
- BDD scenario green in CI.
- OTel counters `ino.l1.proposals_emitted` / `ino.l1.approved` / `ino.l1.activated` populated.

**Closes.** [#25](https://github.com/LeftTwixWand/ino/issues/25).

**Out of scope for the slice.**
- Cross-user proposal aggregation (single-user clusters only — cross-user is post-v0.1).
- L2 reasoning-time C# (separate slice; the Roslyn pipeline here is reusable).
- L3 compiled-silo restart loop (post-v0.1).
- Marketplace promotion (high-confidence proposal → default-install for new users) — post-v0.1.
- Durable registry — falls out of #22; this slice ships in-memory and documents the gap.

---

## Cross-cutting concerns

### Trace shape across the new domains

Every Phase-4 silo must declare its own OTel `service.name` so the existing cross-domain trace filter rule (CLAUDE.md verification step 4) keeps working: `Ino.Domains.Reminders`, `Ino.Domains.Recall`, `Ino.Domains.Genesis`. Confirm via `Ino.ServiceDefaults` that `AddInoServiceDefaults` injects the assembly name as `service.name`; if not, set it explicitly in each `Program.cs`.

### Per-domain test project shape

Reuse the Phase 3 collocated pattern. Each new domain ships with `Ino.Domains.<X>.Tests` next to its impl, using `[Collection(nameof(InoTestCollection))]` and a per-domain `InoTestCollection.cs` deriving from `Ino.Testing.InoTestCollection`. Do NOT add tests to `test/` for a per-domain feature — `test/` is reserved for kernel-level + cross-domain E2E.

### `[PinToSilo]` policy

The Phase 3 lesson stands: per-domain grains route via assembly scoping; do NOT pin them. `[PinToSilo]` is reserved for cluster singletons. In Phase 4 only `CreatorNeuron` (Slice E) gets pinned, because registry mutations must serialize.

### What does NOT change

- `Ino.Core` and `Ino.Core.Hosting` public surface beyond the ML namespace addition.
- Travel and Taxi domain shapes beyond adding plan grains in Slice A.
- `IAW.Core.Agent` itself — every IAW capability used here is already in tree as of commit 582ea3c.
- The xAI provider declarations in `Ino.AppHost`. New embedding requirement for Slice C may force one new provider declaration; treat it as a Slice C build item, not a cross-cutting refactor.

---

## Out of scope for Phase 4

Hard list — push back if a review pulls toward any of these:

- #21 topology decision (local/cloud/hybrid) — independent track, no Phase 4 slice depends on the choice.
- #22 durable persistence + cluster membership — Slices A–E ship on the volatile substrate. Slice E explicitly documents the registry-survival gap.
- #23 synapse decay + reinforcement — orthogonal to scheduling/recall/ML.
- L2 reasoning-time C# (the Roslyn pipeline from Slice E is a reusable seam, but the L2 user-facing surface is post-v0.1).
- L3 compiled-silo + rolling restart (post-v0.1).
- Cross-user proposal aggregation, marketplace signing/sandboxing, revenue model (all post-v0.1 per `product-vision-final.md`).
- More IAW bridges beyond Reminders + Recall (approvals, threading, supervisor self-healing) — pattern is established in Slices B + C; subsequent bridges are follow-ups, not gating.
- Telegram / ino-windows full migration.

---

## Dependency graph

```
A (close #24)
└── enables every following slice's plan path

B (Reminders) ──┐
C (Recall)    ──┼── independent, can ship in any order after A
D (NeuronML)  ──┘     (D also depends on whatever ML files actually exist
                       in iaw — first task of Slice D is to verify)

E (Genesis + L1)
├── depends on A (plan dispatch is the activation surface)
├── benefits from C (memory provides "example prompts" payload for proposals)
└── benefits from D (new neurons begin learning from decision 1)
```

Recommended order: **A → B → C → D → E**. Slices B and C can be parallelised across collaborators if Slice A is merged first.

## Roll-forward gate

After Slice E lands and the BDD acceptance is green, Phase 4 is done. Phase 5 candidates: #21 topology decision, #22 durable persistence, #23 synapse decay. Phase 5 plan to be drafted once #21 picks a topology.
