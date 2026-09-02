# DigitalBrain v2 Static Neuron Substrate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the static DigitalBrain substrate so a neuron has one actor responsibility, signal delivery reports an explicit outcome, graph learning occurs only after handled traffic, dependencies are explicit, and kernel contracts contain only kernel vocabulary.

**Architecture:** `Neuron` remains the serialized Orleans aggregate and implements the delivery and observation ports. A constructor-injected `NeuronRuntime` is the composition boundary for activation-scoped journal state and shared routing/time services; `SignalDispatcher` owns handler discovery/invocation and `SignalSender` owns neuron-originated outbound journaling, delivery, replies, and learning. `BrainNeuron` is the owner's root graph actor; `IDigitalBrain` remains its logic-free client-side handle. In later slices, only graph endpoints such as `AutomationNeuron` use this substrate. Definition, execution-run, and effect-worker grains remain separate pre-registered aggregate roles and do not implement `INeuron` or receive `NeuronRuntime`.

**Tech Stack:** .NET 11, C# latest, Orleans 10.2.2, Orleans Journaling 10.2.2-rc.2.alpha.1, Microsoft.Extensions.DependencyInjection, xUnit v3, Microsoft Testing Platform, PowerShell, XML solution (`DigitalBrain.slnx`).

**Spec:** [`docs/v2-rebuild-brief.md`](../../v2-rebuild-brief.md), followed by [`docs/superpowers/specs/2026-09-02-digitalbrain-v2-durable-runs-design.md`](../specs/2026-09-02-digitalbrain-v2-durable-runs-design.md).

## Global Constraints

- Treat `docs/v2-rebuild-brief.md` as binding for this slice. The durable-runs design constrains future compatibility but does not authorize run engines, task agents, automations, AI orchestration, code generation, effectors, or durable jobs here.
- Keep `net11.0`, `TreatWarningsAsErrors=true`, `AnalysisLevel=preview-all`, and `EnforceCodeStyleInBuild=true` green after every task.
- Keep package versions in `Directory.Packages.props`; never put `Version` on a `PackageReference`.
- Add every new project to `DigitalBrain.slnx`.
- Every new serialized data type must carry `[GenerateSerializer]` and one stable `[Alias("db.…")]`; preserve existing aliases when moving data contracts between assemblies or namespaces. This v2 cutover intentionally changes the behavior-bearing interface aliases to `db.v2.neuron`, `db.v2.neuron-query`, and `db.v2.brain-neuron` because their method contracts change; do not change any persisted grain type, grain key, state key, or data-contract alias as part of that cutover.
- Do not add `[Reentrant]`, `[StatelessWorker]`, `[MayInterleave]`, or broaden interleaving beyond methods declared by `INeuronQuery`.
- Preserve signal lineage, principal propagation, journal-before-delivery ordering, and serialized turns.
- Keep routing and authority separate: a handler declaration or synapse can select a receiver but can never grant a capability.
- Treat incoming/outgoing neuron journals as observable traffic history, not as a durable-run checkpoint or effect ledger.
- Use one `SignalSender.DeliverAsync` path for neuron-originated directed sends, broadcasts, and detached replies in this slice. Awaited self-delivery dispatches in-process; a detached reply queues the grain call so the current serialized caller can finish.
- Handler exceptions continue to fault delivery. `DeliveryOutcome.Unhandled` means no matching `IHandle<T>` exists; `Refused` is reserved for the later membrane and must not be synthesized in this slice.
- `SignalDeliveryResult` carries the existing delivery envelope plus the new outcome. This preserves correlation for typed replies and lets typed client requests fail immediately when the target is unhandled.
- Keep request/reply journal-correlated. Do not redesign it into a same-RPC response; `ReplyAsync` remains detached to avoid the A-awaits-B/B-awaits-A deadlock.
- Keep `DigitalBrainClient` as a logic-free facade whose methods are one-line delegations. Put unavoidable client mechanics (object-reference lifecycle, polling fallback, cancellation, response correlation, and argument shape checks) in one internal `DigitalBrainClientTransport`; put graph-routing, learning, handler, durable state, and owner-target decisions on `BrainNeuron` and the substrate collaborators.
- Keep the stable durable-state names exactly: `incoming`, `incoming.tally`, `incoming.sequence`, `outgoing`, `outgoing.tally`, `outgoing.sequence`, `synapses`, and all module-owned state keys.
- Orleans Journaling exposes durable collections as keyed, activation-scoped services. In this slice, `NeuronRuntime.Bind` is the single infrastructure composition boundary allowed to resolve the seven core neuron streams. It is a stateless singleton: it must never retain a `NeuronJournal`, `SynapseSet`, durable collection, or any other activation-scoped object; only the immutable components returned to one activation retain them. Existing module-owned durable-state composition remains unchanged until its module is refactored. `Neuron`, `Entity`, `NeuronJournal`, `NeuronFeed`, and `SynapseSet` receive concrete collaborators and contain no service-location calls or dependency fallbacks.
- In Slice 1, pruning means excluding a decayed edge from both graph reads and routing. Delete the uncalled physical `SynapseSet.Prune()` sweep; do not add a reminder or reclamation subsystem.
- Keep `Synapse.SignalType` on the current CLR-name key in this slice. Stable alias catalogs belong to later static-index/dynamic-capability work; do not preserve the dead `SignalAlias`/`SignalTypeIndex` implementation to anticipate it.
- Do not force the later durable outbox through `SignalSender`: it always creates a fresh neuron traffic envelope. A later `DurableSignalOutboxDispatcher` will accept an already-journaled envelope with its stable `SignalId` and retry it without restaging; the run persists a graph-facing caller (`BrainNeuron` for a task-agent run or the owning `AutomationNeuron` for an automation run). That transport, acknowledgement deduplication, and run identity fields are Slice 2 work, and no run aggregate becomes a neuron to obtain them.
- Run the full solution build and the serialized full test command after every task:

  ```powershell
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

---

## File Map

### Create

- `src/Kernel/DigitalBrain.Contracts/Neurons/INeuronQuery.cs` — free observation plane.
- `src/Kernel/DigitalBrain.Contracts/Signals/DeliveryOutcome.cs` — handled/unhandled/refused wire outcome.
- `src/Kernel/DigitalBrain.Contracts/Signals/SignalDeliveryResult.cs` — delivery envelope plus outcome for directed sends.
- `src/Kernel/DigitalBrain/Neuron/NeuronRuntime.cs` — injected composition boundary and activation component binding.
- `src/Kernel/DigitalBrain/Neuron/SignalDispatcher.cs` — cached `IHandle<T>` discovery and invocation.
- `src/Kernel/DigitalBrain/Neuron/SignalSender.cs` — the only neuron-originated outbound staging/delivery/learning implementation in Slice 1.
- `src/Kernel/DigitalBrain.Client/DigitalBrainClientTransport.cs` — client-only validation, observer, polling, and correlation mechanics behind the logic-free facade.
- `src/Kernel/DigitalBrain.Client/SignalDeliveryRefusedException.cs` — neutral typed-request failure for a membrane-refused delivery.
- `tests/DigitalBrain.Substrate.Tests/ContractShapeTests.cs` — contract-plane and wire-metadata tests.
- `tests/DigitalBrain.Substrate.Tests/SignalDispatcherTests.cs` — dispatcher unit tests independent of Orleans activation.
- `tests/DigitalBrain.Substrate.Tests/ManualTimeProvider.cs` — deterministic test clock.
- `tests/DigitalBrain.Simulation.Tests/ContractOwnershipTests.cs` — assembly/package boundary tests.
- `src/Product/DigitalBrain.Product.Contracts/DigitalBrain.Product.Contracts.csproj` — neutral product contracts used across UI, Time, AI, providers, and SDK.
- `src/Product/DigitalBrain.Product.Contracts/Identity/CommandId.cs`.
- `src/Product/DigitalBrain.Product.Contracts/Interactions/AgentTurnContext.cs`.
- `src/Product/DigitalBrain.Product.Contracts/Interactions/ITrustedUserCommandHandler.cs`.
- `src/Product/DigitalBrain.Product.Contracts/Interactions/IUntrustedContentScreen.cs`.
- `src/Product/DigitalBrain.Product.Contracts/Interactions/IUserActionContinuation.cs`.
- `src/Product/DigitalBrain.Product.Contracts/Interactions/IUserActionSource.cs`.
- `src/Product/DigitalBrain.Product.Contracts/Interactions/UserActionRequest.cs`.

### Rename or move

- `src/Kernel/DigitalBrain.Contracts/Neurons/ISessionNeuron.cs` → `src/Kernel/DigitalBrain.Contracts/Neurons/IBrainNeuron.cs`.
- `src/Kernel/DigitalBrain/Neuron/SessionNeuron.cs` → `src/Kernel/DigitalBrain/Neuron/BrainNeuron.cs`.
- `src/Kernel/DigitalBrain.Contracts/Messaging/DigitalBrainActivated.cs` → `src/Kernel/DigitalBrain.Contracts/Signals/DigitalBrainActivated.cs`.
- `src/Kernel/DigitalBrain.Contracts/Messaging/JournalProjectionAttribute.cs` → `src/Kernel/DigitalBrain.Contracts/Signals/JournalProjectionAttribute.cs`.
- `src/Kernel/DigitalBrain.Contracts/Execution/ExecutionId.cs` → `src/Modules/Execution/Contracts/ExecutionId.cs`.
- `src/Kernel/DigitalBrain.Contracts/Execution/ContextPath.cs` → `src/Modules/Execution/Contracts/ContextPath.cs`.
- `src/Kernel/DigitalBrain.Contracts/Execution/ContextDigest.cs` → `src/Modules/Execution/Contracts/ContextDigest.cs`.
- `src/Kernel/DigitalBrain.Contracts/Security/ProtectedPayloadReference.cs` → `src/Modules/Memory/Contracts/ProtectedPayloadReference.cs`.

### Delete

- `src/Kernel/DigitalBrain/SignalTypeIndex.cs`.
- `src/Kernel/DigitalBrain/SignalAlias.cs`.
- `src/Kernel/DigitalBrain/Neuron/NeuronTime.cs`.
- `src/Kernel/DigitalBrain.Contracts/Identity/ModuleId.cs`.
- `src/Kernel/DigitalBrain.Contracts/Brain/Unrouted.cs` after `DeliveryOutcome` replaces its never-produced client refusal path.
- The `SynapseSet.Prune()` method, not the `SynapseSet` type.

### Core modifications

- `src/Kernel/DigitalBrain.Contracts/Neurons/INeuron.cs` — delivery only, returning `DeliveryOutcome`.
- `src/Kernel/DigitalBrain.Contracts/Signals/SignalDelivery.cs` — require an explicit clock in its factory.
- `src/Kernel/DigitalBrain.Contracts/Synapses/Synapse.cs` — potentiate from decayed weight.
- `src/Kernel/DigitalBrain/Neuron/Neuron.cs` — identity/lifecycle/interfaces and inbound turn orchestration only.
- `src/Kernel/DigitalBrain/Neuron/NeuronConcurrency.cs` — whitelist by `INeuronQuery` declaring interface.
- `src/Kernel/DigitalBrain/Neuron/NeuronFeed.cs` — concrete durable-state constructor.
- `src/Kernel/DigitalBrain/Neuron/NeuronJournal.cs` — concrete feeds and neuron identity constructor.
- `src/Kernel/DigitalBrain/Neuron/SynapseSet.cs` — concrete state/options/clock constructor, handled-only recording, consistent read pruning.
- `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs` — clock/runtime registrations with no fallback.
- `src/Kernel/DigitalBrain/Entities/Entity.cs` — remove the unused clock service lookup.
- `src/Kernel/DigitalBrain.Client/{IDigitalBrain,DigitalBrainClient,NeuronReference}.cs` — `SendAsync` vocabulary, implicit-address query projections, result/outcome propagation, and one-line `BrainNeuron` delegation.
- Every concrete `Neuron` subclass in Kernel, modules, console, and substrate tests — accept and forward `NeuronRuntime`.
- Former `EmitAsync` callers in Execution, Time, and UI — either use `RecordOutgoingAsync` or remove a duplicate record after `ReplyAsync`.
- Project files and `DigitalBrain.slnx` — correct contract ownership references.

---

### Task 1: Split Delivery and Observation Contracts

**Files:**

- Create: `src/Kernel/DigitalBrain.Contracts/Neurons/INeuronQuery.cs`
- Create: `src/Kernel/DigitalBrain.Contracts/Signals/DeliveryOutcome.cs`
- Create: `tests/DigitalBrain.Substrate.Tests/ContractShapeTests.cs`
- Modify: `src/Kernel/DigitalBrain.Contracts/Neurons/INeuron.cs`
- Modify: `src/Kernel/DigitalBrain.Contracts/Signals/SignalDelivery.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/Neuron.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/NeuronConcurrency.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/SessionNeuron.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/Surface/SurfaceBoot.cs`
- Modify: `tests/DigitalBrain.Substrate.Tests/{NeuronConcurrencyTests,SignalRoutingTests,SynapseSetTests}.cs`

**Interfaces:**

- Consumes: existing `Signal`, `SignalDelivery`, `JournalRead`, `Synapse`, and observer contracts; Orleans `IGrainWithStringKey`, `[ReadOnly]`, and `[AlwaysInterleave]`.
- Produces: `INeuron.Deliver(SignalDelivery, CancellationToken) : Task<DeliveryOutcome>`; `INeuronQuery.ReadJournal`, `ReadSynapses`, `Watch`, and `Unwatch`; and the exact clock-first `SignalDelivery.Create` overload defined in Step 5.

- [ ] **Step 1: Add the failing contract-shape tests.**

  Add tests which compile against the target types and assert exact ownership:

  ```csharp
  [Fact]
  public void NeuronContractsSeparateDeliveryFromObservation()
  {
      Assert.Equal([nameof(INeuron.Deliver)],
          typeof(INeuron).GetMethods().Select(static method => method.Name).Order().ToArray());
      Assert.Equal(typeof(Task<DeliveryOutcome>),
          typeof(INeuron).GetMethod(nameof(INeuron.Deliver))!.ReturnType);
      Assert.Equal(
          [nameof(INeuronQuery.ReadJournal), nameof(INeuronQuery.ReadSynapses),
           nameof(INeuronQuery.Unwatch), nameof(INeuronQuery.Watch)],
          typeof(INeuronQuery).GetMethods().Select(static method => method.Name).Order().ToArray());
  }

  [Fact]
  public void DeliveryOutcomeIsAnAliasedWireType()
  {
      Assert.NotNull(typeof(DeliveryOutcome).GetCustomAttribute<GenerateSerializerAttribute>());
      var alias = Assert.Single(typeof(DeliveryOutcome).GetCustomAttributes<AliasAttribute>());
      Assert.Equal("db.v2.delivery-outcome", alias.Alias);
  }

  [Fact]
  public void NeuronPortsUseTheIntentionalV2Aliases()
  {
      Assert.Equal(
          "db.v2.neuron",
          Assert.Single(typeof(INeuron).GetCustomAttributes<AliasAttribute>()).Alias);
      Assert.Equal(
          "db.v2.neuron-query",
          Assert.Single(typeof(INeuronQuery).GetCustomAttributes<AliasAttribute>()).Alias);
  }
  ```

- [ ] **Step 2: Run the focused test and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ContractShapeTests"
  ```

  Expected: compilation fails because `INeuronQuery` and `DeliveryOutcome` do not exist.

- [ ] **Step 3: Add the target contracts with stable Orleans metadata.**

  Implement `INeuronQuery` exactly as the approved brief: alias `db.v2.neuron-query`; `ReadJournal` and `ReadSynapses` carry both `[ReadOnly]` and `[AlwaysInterleave]`; `Watch` and `Unwatch` remain serialized observer-management calls. Change `INeuron` alias to `db.v2.neuron` and leave only:

  ```csharp
  Task<DeliveryOutcome> Deliver(
      SignalDelivery delivery,
      CancellationToken cancellationToken = default);
  ```

  Add:

  ```csharp
  [GenerateSerializer]
  [Alias("db.v2.delivery-outcome")]
  public enum DeliveryOutcome : byte
  {
      Handled,
      Unhandled,
      Refused,
  }
  ```

- [ ] **Step 4: Make outcome reporting real before extracting it.**

  Have the current reflection dispatch return `Handled` only after a matching handler completes, and `Unhandled` after the no-handler path. Append incoming traffic for both outcomes; continue to rethrow handler failures. Change `Neuron` to implement both `INeuron` and `INeuronQuery`.

- [ ] **Step 5: Make the clock explicit at envelope creation.**

  Change `SignalDelivery.Create` so `TimeProvider timeProvider` is required and null-checked instead of optional with `TimeProvider.System` fallback. Use this exact compilable order so no required parameter follows an optional one:

  ```csharp
  public static SignalDelivery Create(
      Signal signal,
      NeuronId caller,
      long sequence,
      TimeProvider timeProvider,
      SignalDelivery? cause = null,
      CorrelationId? correlation = null,
      PrincipalId? principal = null)
  ```

  Keep correlation, causation, sequence, principal, field IDs, and alias unchanged. Update the sole production call in `Neuron`.

- [ ] **Step 6: Whitelist the observation plane by declaring type.**

  Replace method-name comparisons in `NeuronConcurrency.IsKernelFreeRead` with:

  ```csharp
  private static bool IsKernelFreeRead(MethodInfo method)
      => method.DeclaringType == typeof(INeuronQuery);
  ```

  Update the guardrail error message and the test stub so it implements both interfaces. Add one assertion proving an independently declared `[ReadOnly]` method is still refused.

- [ ] **Step 7: Migrate query callers without weakening domain ports.**

  Keep domain interfaces such as `IAnnouncer` and `IPingSource` derived from `INeuron` only. Query the same grain ID through `INeuronQuery` in substrate tests and through `GrainFactory.GetGrain<INeuronQuery>` in the root neuron. Do not make every domain command interface inherit the query interface.

  `SurfaceBoot.OnSubscribed` is the one callback which currently returns `Deliver` as a plain `Task`; change it now to an async callback which awaits `Task<DeliveryOutcome>` and discards the result. This keeps Task 1 independently buildable after the contract return type changes.

- [ ] **Step 8: Run the focused suite, then the full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

  Expected: contract tests pass and all 141 pre-existing tests remain green.

- [ ] **Step 9: Commit the contract plane.**

  ```powershell
  git add src/Kernel/DigitalBrain.Contracts src/Kernel/DigitalBrain/Neuron tests/DigitalBrain.Substrate.Tests
  git commit -m "refactor: split neuron delivery and query contracts"
  ```

---

### Task 2: Introduce the Runtime Composition Boundary

**Files:**

- Create: `src/Kernel/DigitalBrain/Neuron/NeuronRuntime.cs`
- Create: `tests/DigitalBrain.Substrate.Tests/ManualTimeProvider.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/{Neuron,NeuronFeed,NeuronJournal,SynapseSet}.cs`
- Modify: `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs`
- Modify: `src/Kernel/DigitalBrain/Entities/Entity.cs`
- Delete: `src/Kernel/DigitalBrain/Neuron/NeuronTime.cs`
- Modify: all concrete `Neuron` subclasses listed below
- Modify: `tests/DigitalBrain.Substrate.Tests/SynapseSetTests.cs`

**Interfaces:**

- Consumes: the Task 1 delivery/query ports, Orleans Journaling's seven keyed activation-scoped core states, `TimeProvider`, `SignalRouter`, and `SynapseOptions`.
- Produces: `NeuronRuntime(TimeProvider, SignalRouter, SynapseOptions)`; `NeuronRuntime.Bind(IServiceProvider, NeuronId) : NeuronActivationComponents`; `NeuronActivationComponents(TimeProvider Clock, SignalRouter Router, NeuronJournal Journal, SynapseSet Synapses)`; and `protected Neuron(NeuronRuntime runtime)`.

- [ ] **Step 1: Add a failing integration test for the configured clock.**

  Add this deterministic clock:

  ```csharp
  internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
  {
      private DateTimeOffset _utcNow = utcNow;

      public override DateTimeOffset GetUtcNow() => _utcNow;

      internal void Advance(TimeSpan elapsed) => _utcNow += elapsed;
  }
  ```

  Then add `ConfiguredClock_StampsOutgoingDelivery` (use the existing `PingSource` fixture and its `INeuronQuery` proxy):

  ```csharp
  var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
  await using var brain = await BrainSimulation.StartAsync(new()
  {
      Modules = new([]),
      ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
  });

  var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "clock");
  var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "clock");
  var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
  var query = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());

  await source.SendTo(sinkId, "timestamp");

  var delivery = Assert.Single((await query.ReadJournal(JournalKind.Outgoing, 0)).Delta);
  Assert.Equal(clock.GetUtcNow(), delivery.Timestamp);
  ```

- [ ] **Step 2: Run that test and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConfiguredClock"
  ```

  Expected: the timestamp comes from `TimeProvider.System`, so equality fails.

- [ ] **Step 3: Add `NeuronRuntime` as the only injected kernel dependency.**

  Use the approved public dependency surface:

  ```csharp
  public sealed class NeuronRuntime
  {
      public NeuronRuntime(TimeProvider clock, SignalRouter router, SynapseOptions options)
      {
          ArgumentNullException.ThrowIfNull(clock);
          ArgumentNullException.ThrowIfNull(router);
          ArgumentNullException.ThrowIfNull(options);
          Clock = clock;
          Router = router;
          Options = options;
      }

      internal TimeProvider Clock { get; }
      internal SignalRouter Router { get; }
      internal SynapseOptions Options { get; }
  }

  internal sealed record NeuronActivationComponents(
      TimeProvider Clock,
      SignalRouter Router,
      NeuronJournal Journal,
      SynapseSet Synapses);
  ```

  Add an internal `Bind(IServiceProvider activationServices, NeuronId owner)` method which resolves the seven named core durable states once and returns an immutable activation-components object containing the clock, router, `NeuronJournal`, and `SynapseSet`. This is the only new service-provider boundary for core neuron state; use `GetRequiredService`/`GetRequiredKeyedService` and never a fallback. `NeuronRuntime` must not cache or retain any returned component because the runtime is singleton while each durable collection is activation-scoped.

- [ ] **Step 4: Give state collaborators concrete dependencies.**

  Replace service-provider constructors with these signatures:

  ```csharp
  NeuronFeed(
      IDurableList<byte[]> retained,
      IDurableDictionary<string, long> tallies,
      IDurableValue<long> lastSequence,
      Serializer<JournalEntry> entries)

  NeuronJournal(NeuronId neuronId, NeuronFeed incoming, NeuronFeed outgoing)

  SynapseSet(
      IDurableDictionary<string, Synapse> synapses,
      SynapseOptions options,
      NeuronId owner,
      TimeProvider clock)
  ```

  Preserve every durable-state key. Replace the journal's `Neuron` reference with `NeuronId` for watcher-drop telemetry.

- [ ] **Step 5: Register required defaults once, without silent construction.**

  In `DigitalBrainRuntime.Add`, register in this order:

  ```csharp
  builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
  builder.Services.TryAddSingleton<SynapseOptions>();
  builder.Services.TryAddSingleton<SignalHandlerIndex>();
  builder.Services.TryAddSingleton<SignalRouter>();
  builder.Services.TryAddSingleton<NeuronRuntime>();
  ```

  A test/host may add a later `TimeProvider` registration to override the default. Do not use a keyed sentinel.

- [ ] **Step 6: Inject and bind the runtime in `Neuron`.**

  Replace the parameterless base constructor with `protected Neuron(NeuronRuntime runtime)`. Bind activation components once, expose `protected TimeProvider TimeProvider => components.Clock`, and use the bound journal/synapse/router. `Neuron.cs` must contain no `GetService`, `GetRequiredService`, `GetKeyedService`, or `GetRequiredKeyedService` call.

- [ ] **Step 7: Remove the unused Entity clock seam.**

  `Entity<TState>.TimeProvider` has no caller. Delete its property and lookup instead of adding a new constructor dependency. Retain only the persistent-state facet and state read/write behavior.

- [ ] **Step 8: Forward `NeuronRuntime` through kernel and product neurons.**

  Update these source classes without otherwise changing their behavior:

  - `src/Kernel/DigitalBrain/Neuron/SessionNeuron.cs`
  - `src/Modules/AI/AI/Agent.cs`
  - `src/Modules/AI/AI/Assistant.cs`
  - `src/Modules/Execution/Execution/ExecutionNeuron.cs`
  - `src/Modules/Memory/Memory/VectorMemoryNeuron.cs`
  - `src/Modules/Time/Time/TimerNeuron.cs`
  - `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs`
  - `src/Modules/UI/DigitalBrain.Modules.UI/Chat/ChatTurnWorker.cs`
  - `src/Modules/UI/DigitalBrain.Modules.UI/Render/UIRenderer.cs`
  - `src/Modules/UI/DigitalBrain.Modules.UI/Surface/SurfaceBoot.cs`
  - `src/DigitalBrainConsole/{ChatNeuron,GreeterNeuron,LoggerNeuron}.cs`

  Preserve each class's existing module-specific dependency resolution for now; this task changes only the kernel dependency boundary. `Agent` accepts `(NeuronRuntime runtime, IChatClient chatClient)`, and `Assistant` forwards both.

- [ ] **Step 9: Forward `NeuronRuntime` through substrate fixture neurons.**

  Update `Announcer`, `EarA`, `EarB`, `Gossip`, `EarC`, `PingSource`, `PingSink`, and any new silent/outcome fixtures in the substrate tests. Use concise primary constructors where the fixture has no other constructor work.

- [ ] **Step 10: Delete `NeuronTime` and prove the boundary.**

  ```powershell
  rg -n "NeuronTime|Get(Service|KeyedService)|GetRequired(Service|KeyedService)" src/Kernel/DigitalBrain/Neuron/Neuron.cs src/Kernel/DigitalBrain/Entities/Entity.cs src/Kernel/DigitalBrain/Neuron/NeuronJournal.cs src/Kernel/DigitalBrain/Neuron/SynapseSet.cs
  ```

  Expected: no matches.

- [ ] **Step 11: Run the focused clock test, then the full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConfiguredClock"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

  Expected: the journal timestamp equals the fake clock and the full suite is green.

- [ ] **Step 12: Commit runtime injection.**

  ```powershell
  git add src tests/DigitalBrain.Substrate.Tests
  git commit -m "refactor: inject the neuron runtime"
  ```

---

### Task 3: Extract Handler Dispatch

**Files:**

- Create: `src/Kernel/DigitalBrain/Neuron/SignalDispatcher.cs`
- Create: `tests/DigitalBrain.Substrate.Tests/SignalDispatcherTests.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/{Neuron,NeuronRuntime,SessionNeuron}.cs`

**Interfaces:**

- Consumes: `DeliveryOutcome`, `IHandle<TSignal>.HandleAsync`, and Task 2's activation components.
- Produces: `SignalDispatcher.DispatchAsync(object neuron, Signal signal, CancellationToken cancellationToken) : Task<DeliveryOutcome>` and one singleton dispatcher reference added to `NeuronActivationComponents`.

- [ ] **Step 1: Write independent failing dispatcher tests.**

  Define a plain `RecordingHandler : IHandle<Ping>` and a plain object with no handler. Assert:

  ```csharp
  Assert.Equal(DeliveryOutcome.Handled,
      await dispatcher.DispatchAsync(handler, new Ping("handled"), CancellationToken.None));
  Assert.Equal(DeliveryOutcome.Unhandled,
      await dispatcher.DispatchAsync(new object(), new Ping("ignored"), CancellationToken.None));
  ```

  Add a handler which throws a sentinel exception and assert the same exception instance escapes, not `TargetInvocationException`.

- [ ] **Step 2: Run the focused test and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SignalDispatcherTests"
  ```

  Expected: compilation fails because `SignalDispatcher` does not exist.

- [ ] **Step 3: Move reflection and caching into `SignalDispatcher`.**

  Move `HandlerInvoker`, `HandlersByNeuronType`, `HandlersFor`, and `BuildHandlers` out of `Neuron`. Implement:

  ```csharp
  internal async Task<DeliveryOutcome> DispatchAsync(
      object neuron,
      Signal signal,
      CancellationToken cancellationToken)
  {
      if (!HandlersFor(neuron.GetType()).TryGetValue(signal.GetType(), out var handler))
      {
          return DeliveryOutcome.Unhandled;
      }

      await handler(neuron, signal, cancellationToken)
          .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
      return DeliveryOutcome.Handled;
  }
  ```

  Keep `BindingFlags.DoNotWrapExceptions` and the per-neuron-type concurrent cache.

- [ ] **Step 4: Compose and use the dispatcher.**

  Let the singleton `NeuronRuntime` own one internal `SignalDispatcher` instance and include it in the bound activation components. `Neuron.DispatchDeliveryAsync` retains telemetry, verified-principal scope, current-delivery restoration, incoming journaling, persistence, and watcher notification, but delegates handler selection/invocation to the dispatcher.

  ```csharp
  internal sealed record NeuronActivationComponents(
      TimeProvider Clock,
      SignalRouter Router,
      NeuronJournal Journal,
      SynapseSet Synapses,
      SignalDispatcher Dispatcher);
  ```

- [ ] **Step 5: Delete the no-op unbound hook.**

  Remove `Neuron.OnUnboundSignalAsync` and the `SessionNeuron` override. The root neuron still accepts reply delivery into its incoming journal; it correctly reports `Unhandled` because it did not run a handler.

- [ ] **Step 6: Run dispatcher tests and the full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SignalDispatcherTests"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

- [ ] **Step 7: Commit the dispatch extraction.**

  ```powershell
  git add src/Kernel/DigitalBrain/Neuron tests/DigitalBrain.Substrate.Tests/SignalDispatcherTests.cs
  git commit -m "refactor: extract signal dispatch from neuron"
  ```

---

### Task 4: Extract Sending, Collapse the Verbs, and Gate Learning

**Files:**

- Create: `src/Kernel/DigitalBrain.Contracts/Signals/SignalDeliveryResult.cs`
- Create: `src/Kernel/DigitalBrain/Neuron/SignalSender.cs`
- Create: `src/Kernel/DigitalBrain.Client/DigitalBrainClientTransport.cs`
- Create: `src/Kernel/DigitalBrain.Client/SignalDeliveryRefusedException.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/{Neuron,NeuronRuntime,SessionNeuron}.cs`
- Modify: `src/Kernel/DigitalBrain.Contracts/Neurons/ISessionNeuron.cs`
- Modify: `src/Kernel/DigitalBrain.Client/{IDigitalBrain,DigitalBrainClient,NeuronReference}.cs`
- Modify: `src/Modules/{Execution,Time,UI,Memory}/**/*.cs` former outbound call sites
- Modify: `src/DigitalBrainConsole/{Brain,Program,ChatNeuron}.cs`
- Modify: `src/Kernel/DigitalBrain.Silo/MapOwnerCommands.cs`
- Modify: client-query callers in MCP, Silo, Testing, console, and tests
- Modify: substrate and simulation tests using `FireAsync`/`EmitAsync`
- Delete: `src/Kernel/DigitalBrain.Contracts/Brain/Unrouted.cs`

**Interfaces:**

- Consumes: Task 1's delivery/outcome vocabulary, Task 2's bound journal/synapse/clock/router components, Task 3's dispatcher outcome, and Orleans `IGrainFactory`.
- Produces: `SignalDeliveryResult`; the four-method `SignalSender` surface in Step 4; `ISessionNeuron.Send(NeuronId, Signal) : Task<SignalDeliveryResult>`; outcome-returning `IDigitalBrain.SendAsync`/`NeuronReference.SendAsync`; implicit root/reference query projections; and the internal `DigitalBrainClientTransport`.

- [ ] **Step 1: Add failing delivery-behavior tests.**

  Extend the substrate fixtures with a real neuron that has no `IHandle<Ping>`, a neuron which records a journal-only fact, and a neuron which sends `Ping` to itself. Add these named tests:

  - `DirectedSend_HandledTargetReturnsHandledAndLearns`;
  - `DirectedSend_HandlerlessTargetReturnsUnhandledAndDoesNotLearn`;
  - `Broadcast_ReachesHandledAndUnhandledTargetsButLearnsOnlyHandled`;
  - `RecordOutgoing_JournalsWithoutDeliveringOrLearning`;
  - `DirectedSend_ToSelfUsesLocalPath`;
  - `TypedRequest_UnhandledTargetFailsBeforeWaiting`;
  - `FacadeAndNeuronReferenceQueriesUseImplicitSubjects`.

  Add a simulation assertion that one `StartTimer` command produces exactly one matching `TimerScheduled` entry in the timer's outgoing journal. It must fail today because `ReplyAsync` and `EmitAsync` both record the same fact.

  Pin the central handler-less case with this shape:

  ```csharp
  var outcome = await source.SendTo(silentId, "ignored");

  Assert.Equal(DeliveryOutcome.Unhandled, outcome);
  Assert.Empty(await sourceQuery.ReadSynapses());
  Assert.Single((await sourceQuery.ReadJournal(JournalKind.Outgoing, 0)).Delta);
  Assert.Single((await silentQuery.ReadJournal(JournalKind.Incoming, 0)).Delta);
  ```

  Pin the journal-only and self-call cases with:

  ```csharp
  await journalProbe.Record("observed");
  Assert.Single((await journalProbeQuery.ReadJournal(JournalKind.Outgoing, 0)).Delta);
  Assert.Empty(await journalProbeQuery.ReadSynapses());

  Assert.Equal(DeliveryOutcome.Handled, await selfSender.SendSelf("loopback"));
  ```

  For the mixed broadcast, configure routing to resolve one handler and one handler-less neuron, assert the result is `2`, and assert only the handled receiver appears in the sender's learned synapses. For the typed unhandled request, use a short cancellation budget but assert the immediate `InvalidOperationException` message instead of a cancellation/timeout.

  Change `IPingSource.SendTo` to return `Task<DeliveryOutcome>` so these assertions observe the real directed-send result.

- [ ] **Step 2: Run the new tests and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Send|FullyQualifiedName~Broadcast|FullyQualifiedName~RecordOutgoing|FullyQualifiedName~TypedRequest|FullyQualifiedName~Facade"
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TimerScheduled"
  ```

  Expected: the target APIs do not exist; after only renaming the fixture call, the handler-less target would still mint an edge and the timer journal count would be two.

- [ ] **Step 3: Add the wire result which preserves correlation and outcome.**

  ```csharp
  [GenerateSerializer]
  [Alias("db.v2.signal-delivery-result")]
  public sealed record SignalDeliveryResult(
      [property: Id(0)] SignalDelivery Delivery,
      [property: Id(1)] DeliveryOutcome Outcome);
  ```

  Add wire-metadata assertions beside the Task 1 contract tests.

- [ ] **Step 4: Implement `SignalSender` as the only neuron outbound engine in Slice 1.**

  Give it the source ID, clock, router, journal, synapse set, grain factory, a local-delivery delegate, and a `Func<CancellationToken, ValueTask>` journal persistence delegate. Construct it once per neuron activation; neither `NeuronRuntime` nor any singleton may cache it. Its public internal surface is exactly:

  ```csharp
  Task<SignalDeliveryResult> SendAsync(
      NeuronId receiver, Signal signal, SignalDelivery? cause);
  Task<int> BroadcastAsync(Signal signal, SignalDelivery? cause);
  Task ReplyAsync(Signal response, SignalDelivery handling);
  Task<SignalDelivery> RecordOutgoingAsync(
      Signal signal, SignalDelivery? cause, CorrelationId? correlation = null);
  ```

  `SendAsync` stages/persists the outgoing delivery, awaits delivery, records a learned synapse only for `Handled`, persists that edge, and returns both envelope and outcome. `BroadcastAsync` uses one correlation for the fan-out and, on successful completion, returns the number of distinct receivers resolved and delivery-attempted; it records an edge only for each `Handled` outcome. The mixed handled/unhandled test must return two reached receivers and persist only the handled edge. `ReplyAsync` stages and awaits only outbound-record persistence, then starts an independently observed detached delivery; it never awaits `DeliverAsync`.

- [ ] **Step 5: Put every delivery through one self-aware method.**

  Use one private method with an explicit mode:

  ```csharp
  private Task<DeliveryOutcome> DeliverAsync(
      NeuronId receiver,
      SignalDelivery delivery,
      DeliveryMode mode)
      => mode == DeliveryMode.Awaited && receiver == _source
          ? _deliverLocally(delivery, CancellationToken.None)
          : _grains.GetGrain<INeuron>(receiver.ToGrainId()).Deliver(delivery);
  ```

  Directed sends and broadcasts use `Awaited`; detached replies use `Detached`. The detached observer catches faulted delivery tasks and reports those exceptions through `SignalTelemetry.ReplyDropped`. It ignores every successfully completed outcome: in particular, `Unhandled` is the expected outcome when a reply reaches the handler-less owner root after the unbound hook is removed, and must not produce false dropped-reply telemetry. No other Slice 1 code may call `INeuron.Deliver` for neuron-originated outbound traffic, except the broadcast-channel adapter which is itself an inbound transport edge. The later non-neuron durable-outbox adapter is a separate transport boundary and is not implemented here.

- [ ] **Step 6: Reduce `Neuron` to protected one-line verbs.**

  Construct one `SignalSender` from the bound runtime components. Keep these protected methods only:

  ```csharp
  protected Task<SignalDeliveryResult> SendAsync(NeuronId receiver, Signal signal);
  protected Task<int> BroadcastAsync(Signal signal);
  protected Task ReplyAsync(Signal response);
  protected Task<SignalDelivery> RecordOutgoingAsync(Signal signal);
  ```

  Delete `FireAsync`, both `EmitAsync` overloads, `StageOutgoingAsync`, `ResolveEmissionCorrelation`, `DeliverToAsync`, and `DeliverReplyAsync` from `Neuron`.

- [ ] **Step 7: Update the root/client path to expose the outcome.**

  Rename `ISessionNeuron.Fire` to `Send` and return `Task<SignalDeliveryResult>`. Preserve the root grain's same-owner target guard before it delegates to the sender; a raw grain proxy must not bypass the check. Delete its unused public `Emit`. Rename public client methods from `FireAsync` to `SendAsync`:

  ```csharp
  Task<DeliveryOutcome> SendAsync<TNeuron>(
      string name, Signal signal, CancellationToken cancellationToken = default)
      where TNeuron : INeuron;
  ```

  `NeuronReference<TNeuron>.SendAsync(Signal)` returns `DeliveryOutcome`; its typed overload remains `Task<TResponse>` but uses the delivery result's correlation. Before waiting for a typed reply, require `Handled`; throw a clear `InvalidOperationException` for `Unhandled` and a new neutral `SignalDeliveryRefusedException` for `Refused`. Keep `NeuronAuthorizationException` only for an actual owner/authority violation; a future membrane may refuse for other policy reasons.

  Make `IDigitalBrain` an implicit projection of its owner's root rather than a subject-addressed query bag:

  ```csharp
  Task<JournalRead> ReadJournalAsync(
      JournalKind kind,
      long afterSequence = 0,
      CancellationToken cancellationToken = default);
  IAsyncEnumerable<JournalRead> WatchJournalAsync(
      JournalKind kind,
      long afterSequence = 0,
      CancellationToken cancellationToken = default);
  Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
      CancellationToken cancellationToken = default);
  ```

  Add the same three query projections to `NeuronReference<TNeuron>`, where the subject is always that reference's `Id`. This matches the anatomy usage: `brain.GetSynapsesAsync()` reads the root graph, while `chat.ReadJournalAsync(...)` reads the chat neuron's traffic.

  Move all validation, observer object-reference lifecycle, polling fallback, cancellation, and typed-response correlation from `DigitalBrainClient` into one internal `DigitalBrainClientTransport`. The transport takes `IGrainFactory` and `OwnerId`, and its internal query methods may take an explicit `NeuronId` so both root and typed references reuse them. `DigitalBrainClient` retains only the transport field and expression-bodied one-line delegations for every public/internal operation; it owns no branches or transport loops. Client-side ownership validation is an early error only and must not replace the root grain's same-owner checks.

- [ ] **Step 8: Remove the fictional unrouted-signal path.**

  Delete `Unrouted` and `DigitalBrainClient.RequireNoRefusal`. No code has ever produced `Unrouted`; the explicit target outcome now prevents a typed request from waiting when the target has no handler.

- [ ] **Step 9: Classify every former `EmitAsync` call.**

  Apply these exact decisions:

  | Caller | Change |
  |---|---|
  | root activation | `RecordOutgoingAsync`, then publish that exact envelope to the activation broadcast channel |
  | Execution `ExecutionLifecycle` | `RecordOutgoingAsync` |
  | Chat `TurnLifecycle`, `Responded`, `UserMessaged` | `RecordOutgoingAsync` |
  | UI renderer `SurfaceOpened` | `RecordOutgoingAsync` |
  | Timer `TimerElapsed` reminder fact | `RecordOutgoingAsync` |
  | Timer `TimerScheduled` and `TimerCancelled` immediately after `ReplyAsync` | delete the second record; the reply is already in the outgoing journal |

  Remove cancellation-token arguments from all `ReplyAsync` calls; handlers still honor their token before initiating the reply.

- [ ] **Step 10: Migrate remaining callers.**

  Replace protected and client `FireAsync` calls with `SendAsync` in substrate tests, `VectorMemoryTests`, `TimerReminderTests`, `DigitalBrainConsole/Program.cs`, `DigitalBrainConsole/Brain.cs`, and `DigitalBrain.Silo/MapOwnerCommands.cs`. Keep the outcome-discarding `SurfaceBoot.OnSubscribed` adapter introduced in Task 1.

  Migrate subject-addressed reads to typed references in `DigitalBrain.Mcp/ChatTools.cs`, `DigitalBrain.Silo/{MapChatVoice,MapOwnerCommands,OwnerSessionJournal}.cs`, `DigitalBrainConsole/{Brain,Program}.cs`, and `FacadeTests`. Change `JournalWait.ForAsync` to accept a `NeuronReference<TNeuron>` instead of `(IDigitalBrain, NeuronId)`, then update its E2E/simulation callers. This task's build must have no old `IDigitalBrain` signatures or `FireAsync` implementation left.

- [ ] **Step 11: Prove obsolete verbs and duplicate delivery paths are gone.**

  ```powershell
  rg -n --glob '*.cs' "\b(FireAsync|EmitAsync|StageOutgoingAsync|DeliverReplyAsync|DeliverToAsync)\b" src tests
  rg -n -U --glob '*.cs' "GetGrain<INeuron>[\s\S]{0,200}?\.Deliver\(" src/Kernel/DigitalBrain
  ```

  Expected: the first command has no matches; the second identifies only `SignalSender.DeliverAsync`.

- [ ] **Step 12: Run focused behavior tests and the full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~VectorMemoryTests|FullyQualifiedName~TimerReminderTests"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

- [ ] **Step 13: Commit the outbound engine.**

  ```powershell
  git add src tests
  git commit -m "refactor: unify signal sending and handled learning"
  ```

---

### Task 5: Promote the Owner Root to `BrainNeuron`

**Files:**

- Rename: `src/Kernel/DigitalBrain.Contracts/Neurons/ISessionNeuron.cs` → `IBrainNeuron.cs`
- Rename: `src/Kernel/DigitalBrain/Neuron/SessionNeuron.cs` → `BrainNeuron.cs`
- Modify: `src/Kernel/DigitalBrain.Client/{DigitalBrainClientTransport,DigitalBrainClient,NeuronReference}.cs`
- Modify: `tests/DigitalBrain.Simulation.Tests/JournalSmokeTests.cs`
- Modify: `tests/DigitalBrain.E2E.Tests/BootSmokeTests.cs`
- Modify: remaining tests and comments referring to the session neuron

**Interfaces:**

- Consumes: Task 4's root send/query operations and client transport.
- Produces: `IBrainNeuron` and `BrainNeuron` CLR names with v2 alias `db.v2.brain-neuron`, while `IBrainNeuron.ForOwner(OwnerId)` continues to return `sessionneuron/{owner}/session` and the implementation is explicitly `[GrainType("sessionneuron")]`.

- [ ] **Step 1: Add a failing root-neuron identity test.**

  Add to `ContractShapeTests`:

  ```csharp
  [Fact]
  public void BrainNeuronRenamesTheOwnerRootWithoutChangingItsDurableAddress()
  {
      var owner = new OwnerId("owner");

      // These values are persisted protocol identifiers, not CLR vocabulary.
      Assert.Equal("sessionneuron", IBrainNeuron.GrainTypeName);
      Assert.Equal("session", IBrainNeuron.InstanceName);
      Assert.Equal(
          new NeuronId("sessionneuron", owner, "session"),
          IBrainNeuron.ForOwner(owner));
      var alias = Assert.Single(typeof(IBrainNeuron).GetCustomAttributes<AliasAttribute>());
      Assert.Equal("db.v2.brain-neuron", alias.Alias);
      var grainType = Assert.IsType<GrainTypeAttribute>(
          typeof(BrainNeuron).GetCustomAttribute<GrainTypeAttribute>());
      using var services = new ServiceCollection().BuildServiceProvider();
      Assert.Equal(
          "sessionneuron",
          grainType.GetGrainType(services, typeof(BrainNeuron)).ToString());
      Assert.DoesNotContain(
          typeof(INeuron).Assembly.GetTypes(),
          static type => type.Name.Contains("SessionNeuron", StringComparison.Ordinal));
      Assert.DoesNotContain(
          typeof(Neuron).Assembly.GetTypes(),
          static type => type.Name.Contains("SessionNeuron", StringComparison.Ordinal));
  }
  ```

- [ ] **Step 2: Run the test and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BrainNeuronRenamesTheOwnerRootWithoutChangingItsDurableAddress"
  ```

  Expected: `IBrainNeuron` does not exist.

- [ ] **Step 3: Rename the CLR contract and implementation while preserving durable identity.**

  `IBrainNeuron` extends both `INeuron` and `INeuronQuery`, uses the intentional v2 interface alias `db.v2.brain-neuron`, and exposes the existing activate/send/query-projection operations. Preserve `GrainTypeName = "sessionneuron"` and `InstanceName = "session"`: those strings identify existing grain rows, incoming/outgoing journals, synapses, and `activation-published` state. Mark `BrainNeuron` explicitly with `[GrainType(IBrainNeuron.GrainTypeName)]` so the CLR rename cannot change Orleans placement. Rename the implementation to `BrainNeuron`; do not leave forwarding `SessionNeuron` CLR types. The interface alias is an explicit v2 RPC-contract cutover because its operations changed in Task 4; the stable lowercase grain address is persisted protocol data, not obsolete domain vocabulary.

- [ ] **Step 4: Point the client facade at the root.**

  Rename the transport's private `Session()` helper to `Brain()` and make every activation, send, journal, synapse, watch, and unwatch delegation use `IBrainNeuron.ForOwner(Owner)`. Replace every other root-specific reference in `DigitalBrainClientTransport`, including request setup's `ForOwner` call, the poll/teardown helper parameter types, and the root-interface exclusion in `RequireDomainNeuronContract`. Update `JournalSmokeTests` and `BootSmokeTests` explicitly. Add one private same-owner guard in `BrainNeuron` and call it before send, journal, synapse, watch, and unwatch projection; do not add a caller-supplied owner parameter. The later incoming-call membrane remains responsible for enforcement on raw `INeuronQuery` proxies.

- [ ] **Step 5: Update comments and prove old vocabulary is gone.**

  ```powershell
  rg -n --glob '*.cs' "ISessionNeuron|SessionNeuron|Session\(\)" src tests
  ```

  Expected: no matches.

- [ ] **Step 6: Run the facade tests and full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~FacadeTests|FullyQualifiedName~BrainNeuron"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

- [ ] **Step 7: Commit the root-neuron rename.**

  ```powershell
  git add src tests
  git commit -m "refactor: promote the owner root to brain neuron"
  ```

---

### Task 6: Make Decay and Read-Time Pruning Observable

**Files:**

- Modify: `src/Kernel/DigitalBrain.Contracts/Synapses/Synapse.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/SynapseSet.cs`
- Modify: `tests/DigitalBrain.Substrate.Tests/{SynapseTests,SynapseSetTests}.cs`

**Interfaces:**

- Consumes: Task 2's injected clock/options and Task 4's handled-only calls to `SynapseSet.Record`.
- Produces: unchanged public `Synapse` shape and aliases, with `Potentiate` based on effective decayed weight; `SynapseSet.All` and `For` both exclude pruned edges; no physical `Prune` method.

- [ ] **Step 1: Add failing decay-compounding and read-pruning tests.**

  Add `Record_AfterHalfLifePotentiatesDecayedWeight`. Configure `ManualTimeProvider` and `SynapseOptions` with a one-hour half-life. Send once, advance one hour, send again, and assert the stored weight is `0.5275`:

  ```text
  first send: 0.50 + 0.30 × (1 - 0.50) = 0.65
  one half-life: 0.65 × 0.5 = 0.325
  second send: 0.325 + 0.30 × (1 - 0.325) = 0.5275
  ```

  Add `ReadSynapses_ExcludesPrunedSynapse`. In a fresh simulation, send once, advance until the effective weight is below `PruneFloor`, then assert `ReadSynapses()` excludes the edge.

  Also test `SynapseSet` directly so both read APIs—not only the `Synapse` value object—are covered. Since `IDurableDictionary<K,V>` extends `IDictionary<K,V>`, use this test adapter:

  ```csharp
  private sealed class TestDurableDictionary<TKey, TValue>
      : Dictionary<TKey, TValue>, IDurableDictionary<TKey, TValue>
      where TKey : notnull;
  ```

  Add `ReadApis_ExcludePrunedSynapse`. Seed one decayed learned edge, construct `SynapseSet` with the manual clock/options, and assert:

  ```csharp
  Assert.Empty(synapses.All());
  Assert.Empty(synapses.For(nameof(Ping)));
  ```

  Do not use broadcast as the pruning proof: tier-1 handler discovery would legitimately rediscover the target even after its learned edge decays.

- [ ] **Step 2: Run the tests and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Decay|FullyQualifiedName~Prun"
  ```

  Expected: current potentiation produces `0.755`, and `SynapseSet.All()` still exposes a pruned edge.

- [ ] **Step 3: Potentiate from effective current weight.**

  Change the method to:

  ```csharp
  public Synapse Potentiate(DateTimeOffset now, TimeSpan halfLife, double rate)
  ```

  For learned/discovered edges, compute the base with `WeightAt(now, halfLife)` before applying `w + rate * (1 - w)`. Innate weight does not decay, but still stamps `LastFiredAt` and increments `FireCount`. Update the pure tests with the explicit half-life.

- [ ] **Step 4: Make graph reads agree with routing.**

  Filter `SynapseSet.All()` with the same `IsPrunedAt(now, HalfLife, PruneFloor)` predicate already used by `For()`, then order by effective weight. Keep returning the stored record; callers can call `WeightAt` for the current effective value.

- [ ] **Step 5: Remove the dead physical sweep.**

  Delete `SynapseSet.Prune()`. Do not add a reminder. Add a code comment to `All()`/`For()` stating that Slice 1 pruning is read/routing exclusion and physical reclamation belongs to a later storage-maintenance decision.

- [ ] **Step 6: Run focused tests and the full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Synapse"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

- [ ] **Step 7: Commit synapse semantics.**

  ```powershell
  git add src/Kernel/DigitalBrain.Contracts/Synapses/Synapse.cs src/Kernel/DigitalBrain/Neuron/SynapseSet.cs tests/DigitalBrain.Substrate.Tests
  git commit -m "fix: apply synapse decay before learning and reads"
  ```

---

### Task 7: Extract Neutral Product Contracts

**Files:**

- Create: `src/Product/DigitalBrain.Product.Contracts/**`
- Modify: `DigitalBrain.slnx`
- Delete after move: `src/Kernel/DigitalBrain.Contracts/Interactions/**`
- Delete after move: `src/Kernel/DigitalBrain.Contracts/Identity/CommandId.cs`
- Modify: direct consumer project references and namespaces
- Create/modify: `tests/DigitalBrain.Simulation.Tests/ContractOwnershipTests.cs`

**Interfaces:**

- Consumes: the seven existing product interaction/identity types and their current Orleans aliases.
- Produces: `DigitalBrain.Product.Identity.CommandId` and the six `DigitalBrain.Product.Interactions` contracts from a dependency-neutral `DigitalBrain.Product.Contracts` assembly; all data aliases and field IDs remain unchanged.

- [ ] **Step 1: Add a failing assembly-ownership test.**

  Add:

  ```csharp
  [Fact]
  public void ProductInteractionTypesDoNotBelongToKernelContracts()
  {
      Assert.NotSame(typeof(INeuron).Assembly, typeof(CommandId).Assembly);
      Assert.Same(typeof(CommandId).Assembly, typeof(AgentTurnContext).Assembly);
      Assert.Equal("DigitalBrain.Product.Identity", typeof(CommandId).Namespace);
      Assert.Equal("DigitalBrain.Product.Interactions", typeof(AgentTurnContext).Namespace);
      Assert.DoesNotContain(
          typeof(INeuron).Assembly.GetTypes(),
          static type => type.Namespace?.Contains(".Interactions", StringComparison.Ordinal) == true);
  }
  ```

- [ ] **Step 2: Run the ownership test and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProductInteractionTypes"
  ```

  Expected: the new Product namespaces/project do not exist.

- [ ] **Step 3: Create and register the product-contracts project.**

  Create a packable `Microsoft.NET.Sdk` project with root namespace `DigitalBrain.Product`, a direct `Microsoft.Orleans.Sdk` package reference, and a project reference to `../../Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj`. Add it under a new `/Product/` folder in `DigitalBrain.slnx`.

- [ ] **Step 4: Move the seven product types and preserve wire identity.**

  Move `CommandId` to `DigitalBrain.Product.Identity`. Move all six `Interactions` files to `DigitalBrain.Product.Interactions`. Preserve aliases `db.command-id`, `db.agent-turn-context`, and `db.user-action-request`, all field IDs, and `AgentTurnContext` request-context behavior. Do not leave compatibility forwarding types in Kernel.Contracts.

- [ ] **Step 5: Add direct references for contract consumers.**

  Add `DigitalBrain.Product.Contracts.csproj` references to:

  - `src/Kernel/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`
  - `src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj`
  - `src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj`
  - `src/Modules/AI/AI/DigitalBrain.Modules.AI.csproj`
  - `src/Modules/Google/Google/DigitalBrain.Modules.Google.csproj`
  - `src/Modules/Salesforce/Salesforce/DigitalBrain.Modules.Salesforce.csproj`
  - `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj`
  - `src/Modules/UI/DigitalBrain.Modules.UI/DigitalBrain.Modules.UI.csproj`
  - `src/Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj`
  - `src/Modules/Time/Time/DigitalBrain.Modules.Time.csproj`
  - `src/Modules/Execution/Contracts/DigitalBrain.Modules.Execution.Contracts.csproj`
  - `src/Modules/Execution/Execution/DigitalBrain.Modules.Execution.csproj`
  - `src/Modules/Memory/Contracts/DigitalBrain.Modules.Memory.Contracts.csproj`
  - `tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj`

- [ ] **Step 6: Update product namespaces in bounded batches.**

  First replace `DigitalBrain.Abstractions.Interactions` imports with `DigitalBrain.Product.Interactions` in SDK, AI, Google, Salesforce, UI, Silo, and simulation tests. Then import `DigitalBrain.Product.Identity` wherever `CommandId` is used in UI, Time, Execution, Memory, Silo, MCP, and tests. Keep kernel identity imports for `NeuronId`, `OwnerId`, and `ActorContext`.

- [ ] **Step 7: Prove the dependency direction.**

  ```powershell
  rg -n "DigitalBrain\.Product" src/Kernel/DigitalBrain.Contracts src/Kernel/DigitalBrain
  rg -n "DigitalBrain\.Abstractions\.Interactions" src tests
  Test-Path -LiteralPath src/Kernel/DigitalBrain.Contracts/Identity/CommandId.cs
  ```

  Expected: the first two commands report no matches and `Test-Path` prints `False`. Product.Contracts may reference Kernel.Contracts; Kernel.Contracts and the kernel runtime never reference Product.Contracts.

- [ ] **Step 8: Run the ownership test and full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ContractOwnershipTests"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

- [ ] **Step 9: Commit product contract ownership.**

  ```powershell
  git add DigitalBrain.slnx src/Product src/Kernel src/Modules tests/DigitalBrain.Simulation.Tests
  git commit -m "refactor: extract product interaction contracts"
  ```

---

### Task 8: Return Execution and Memory Types to Their Modules

**Files:**

- Move: `src/Kernel/DigitalBrain.Contracts/Execution/{ExecutionId,ContextPath,ContextDigest}.cs`
- Move: `src/Kernel/DigitalBrain.Contracts/Security/ProtectedPayloadReference.cs`
- Modify: Execution, UI, Google, Salesforce, Memory, and test namespaces/project references
- Modify: `tests/DigitalBrain.Simulation.Tests/ContractOwnershipTests.cs`

**Interfaces:**

- Consumes: existing `ExecutionId`, `ContextPath`, `ContextDigest`, and `ProtectedPayloadReference` data shapes and wire metadata.
- Produces: the first three types in namespace `DigitalBrain.Execution` from `DigitalBrain.Execution.Contracts`, and `ProtectedPayloadReference` in namespace `DigitalBrain.Memory` from `DigitalBrain.Memory.Contracts`; wire aliases and field IDs are unchanged.

- [ ] **Step 1: Add failing module-ownership assertions.**

  ```csharp
  [Fact]
  public void ModuleValueTypesLiveWithTheirPublicModuleContracts()
  {
      Assert.Same(typeof(IExecution).Assembly, typeof(ExecutionId).Assembly);
      Assert.Same(typeof(IVectorMemory).Assembly, typeof(ProtectedPayloadReference).Assembly);
      Assert.Equal("DigitalBrain.Execution", typeof(ExecutionId).Namespace);
      Assert.Equal("DigitalBrain.Memory", typeof(ProtectedPayloadReference).Namespace);
  }
  ```

- [ ] **Step 2: Run the test and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ModuleValueTypes"
  ```

  Expected: the value types still come from Kernel.Contracts.

- [ ] **Step 3: Move execution identities into Execution.Contracts.**

  Move `ExecutionId`, `ContextPath`, and `ContextDigest` to `src/Modules/Execution/Contracts` and namespace `DigitalBrain.Execution`. Preserve aliases and IDs. Remove obsolete `DigitalBrain.Abstractions.Execution` imports inside that project; update UI, Google, Salesforce, and tests to import `DigitalBrain.Execution`.

- [ ] **Step 4: Add the one missing contract dependency without a cycle.**

  Add this to `DigitalBrain.Modules.UI.Contracts.csproj`:

  ```xml
  <ProjectReference Include="../../Execution/Contracts/DigitalBrain.Modules.Execution.Contracts.csproj" />
  ```

  Do not add any Execution.Contracts → UI.Contracts edge. The existing Execution runtime → UI.Contracts reference remains valid.

- [ ] **Step 5: Move protected payload identity into Memory.Contracts.**

  Move `ProtectedPayloadReference` to `src/Modules/Memory/Contracts` and namespace `DigitalBrain.Memory`. Preserve alias `db.protected-payload-reference` and IDs. Remove old security imports from `StoreVectorMemory`, `VectorMemoryMatch`, `IVectorMemoryStore`, and `QdrantVectorMemoryProvider`.

- [ ] **Step 6: Prove old module namespaces are absent from Kernel.Contracts.**

  ```powershell
  rg -n "DigitalBrain\.Abstractions\.(Execution|Security)" src tests
  Get-ChildItem -LiteralPath src/Kernel/DigitalBrain.Contracts -Directory | Select-Object -ExpandProperty Name
  ```

  Expected: no old namespace matches and there are no `Execution` or `Security` directories.

- [ ] **Step 7: Run the ownership test and full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ContractOwnershipTests"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

- [ ] **Step 8: Commit module contract ownership.**

  ```powershell
  git add src/Kernel/DigitalBrain.Contracts src/Modules tests/DigitalBrain.Simulation.Tests
  git commit -m "refactor: return value contracts to their modules"
  ```

---

### Task 9: Finish Signal Vocabulary and Delete Proven Dead Types

**Files:**

- Move: `src/Kernel/DigitalBrain.Contracts/Messaging/{DigitalBrainActivated,JournalProjectionAttribute}.cs`
- Delete: `src/Kernel/DigitalBrain/{SignalAlias,SignalTypeIndex}.cs`
- Delete: `src/Kernel/DigitalBrain.Contracts/Identity/ModuleId.cs`
- Modify: signal consumers, project descriptions, `ContractOwnershipTests.cs`

**Interfaces:**

- Consumes: `DigitalBrainActivated` and `JournalProjectionAttribute` with their existing aliases, plus the proven usage scans for `SignalAlias`, `SignalTypeIndex`, and `ModuleId`.
- Produces: both live messaging types under `DigitalBrain.Abstractions.Signals`; no `DigitalBrain.Abstractions.Messaging` namespace and no dead alias-index/module-identity types.

- [ ] **Step 1: Add a failing kernel-vocabulary test.**

  Add this assertion to `ContractOwnershipTests`:

  ```csharp
  [Fact]
  public void KernelVocabularyHasNoLegacyDomainBuckets()
  {
      Assert.Equal("DigitalBrain.Abstractions.Signals", typeof(DigitalBrainActivated).Namespace);
      Assert.Equal("DigitalBrain.Abstractions.Signals", typeof(JournalProjectionAttribute).Namespace);

      var types = typeof(INeuron).Assembly.GetTypes();
      Assert.DoesNotContain(types, static type =>
          type.Namespace is "DigitalBrain.Abstractions.Messaging"
              or "DigitalBrain.Abstractions.Interactions"
              or "DigitalBrain.Abstractions.Execution"
              or "DigitalBrain.Abstractions.Security");
      Assert.DoesNotContain(types, static type => type.Name is "ModuleId" or "Unrouted");
  }
  ```

- [ ] **Step 2: Run the test and confirm RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~KernelVocabulary"
  ```

  Expected: the two signal-adjacent types still use the Messaging namespace and `ModuleId` remains.

- [ ] **Step 3: Move Messaging into Signals without changing aliases.**

  Move both files and change their namespace to `DigitalBrain.Abstractions.Signals`. Update imports in `BrainNeuron`, `SurfaceBoot`, `Responded`, `JournalSmokeTests`, and `BootSmokeTests`. Preserve `db.digitalbrain-activated`; `JournalProjectionAttribute` remains a non-wire marker.

- [ ] **Step 4: Delete the dead index and identity types.**

  Delete `SignalTypeIndex`, `SignalAlias`, and `ModuleId`. Do not replace them. The later static handler index/dynamic capability catalog will receive its own focused design and implementation.

- [ ] **Step 5: Correct stale package descriptions and comments.**

  Update `DigitalBrain.Client.csproj` to describe `SendAsync` and journal observation without claiming an `EmitAsync` surface. Remove comments which still describe Fire/Emit/Session vocabulary in touched files; do not rewrite historical design documents.

- [ ] **Step 6: Run structural searches.**

  ```powershell
  rg -n --glob '*.cs' "\b(SignalTypeIndex|SignalAlias|ModuleId|NeuronTime|Unrouted|ISessionNeuron|SessionNeuron|FireAsync|EmitAsync)\b" src tests
  rg -n --glob '*.cs' "DigitalBrain\.Abstractions\.(Messaging|Interactions|Execution|Security)" src tests
  rg -n "<PackageReference[^>]+Version=" --glob '*.csproj' .
  ```

  Expected: all three commands have no matches.

- [ ] **Step 7: Run ownership tests and the full gate.**

  ```powershell
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ContractOwnershipTests"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

- [ ] **Step 8: Commit the final vocabulary cleanup.**

  ```powershell
  git add DigitalBrain.slnx src tests
  git commit -m "refactor: finish the v2 substrate vocabulary"
  ```

---

### Task 10: Perform the Definition-of-Done Audit

**Files:** Review all files changed by Tasks 1–9; do not add a compatibility layer to satisfy the audit.

**Interfaces:**

- Consumes: every interface and invariant produced by Tasks 1–9, plus the 141-test baseline and commit `82f8852b` as the review base.
- Produces: no new runtime API; produces a mechanically clean, fully built/tested Slice 1 whose scope is explicitly separated from the later durable-run and capability slices.

- [ ] **Step 1: Verify the worktree diff is mechanically clean.**

  ```powershell
  git diff --check 82f8852b..HEAD
  git status --short
  ```

  Expected: no whitespace errors; only intentional uncommitted review fixes, if any.

- [ ] **Step 2: Verify dependency and delivery invariants.**

  ```powershell
  rg -n "Get(Service|KeyedService)|GetRequired(Service|KeyedService)" src/Kernel/DigitalBrain/Neuron/Neuron.cs src/Kernel/DigitalBrain/Entities/Entity.cs src/Kernel/DigitalBrain/Neuron/NeuronJournal.cs src/Kernel/DigitalBrain/Neuron/SynapseSet.cs
  rg -n --glob '*.cs' "new SignalRouter|\?\?\s*new" src/Kernel/DigitalBrain
  rg -n -U --glob '*.cs' "GetGrain<INeuron>[\s\S]{0,200}?\.Deliver\(" src/Kernel/DigitalBrain
  ```

  Expected: no service-location or fallback matches; neuron-originated outbound delivery in this slice exists only in `SignalSender.DeliverAsync`.

- [ ] **Step 3: Verify graph behavior through named tests.**

  ```powershell
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Handlerless|FullyQualifiedName~ConfiguredClock|FullyQualifiedName~Decay|FullyQualifiedName~Prun|FullyQualifiedName~Self"
  ```

  Expected: handler-less sends create no edge, the fake clock controls runtime time, decay compounds before potentiation, pruned edges disappear from reads/routes, and self-send completes.

- [ ] **Step 4: Run the final clean solution gate.**

  ```powershell
  dotnet clean DigitalBrain.slnx -c Release
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

  Expected: zero warnings, zero errors, zero failed tests. The test total must be greater than the 141-test baseline because this plan adds substrate and ownership coverage.

- [ ] **Step 5: Inspect type usage before declaring completion.**

  For every type still declared in `src/Kernel/DigitalBrain` and `src/Kernel/DigitalBrain.Contracts`, use `rg -n "\bTypeName\b" src tests` and confirm at least one use outside its declaring file. Delete a truly unused type and rerun Step 4; do not retain it for hypothetical future work.

- [ ] **Step 6: Review the Slice 1 boundary.**

  Confirm the diff contains no durable-run state machine, agent/automation definition, dynamic capability catalog, sensor, effector, membrane, similarity routing, Roslyn compilation, `AssemblyLoadContext`, or new grain registration mechanism. Confirm only future graph endpoints such as `AutomationNeuron` would receive `NeuronRuntime`; `ExecutionRunGrain`, definition aggregates, effect workers, and one-off task-agent runs remain non-neuron pre-registered grain roles. Confirm nothing in `SignalSender` is presented as the later transactional-outbox retry mechanism.

- [ ] **Step 7: Apply review fixes, rerun Step 4, and commit only if needed.**

  ```powershell
  git add src tests DigitalBrain.slnx
  git commit -m "test: enforce v2 substrate invariants"
  ```

  Skip this commit when review required no file changes.
