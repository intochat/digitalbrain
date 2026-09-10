# Neurons-Only Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Each task is one Codex session driven by a Claude subagent, followed by a fresh-eyes review. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace this repo's neuron substrate and the applications/scripting subsystem with the converged communication model, keep every module and the Flutter shell working, and land it as one reviewed PR.

**Architecture:** The proven kernel from `E:\intochat\digitalbraincore` (1.7k LOC: Contracts, DigitalBrain, Mcp, Testing) is copied in as the base and extended per the spec: typed commands with an audited command journal, a durable pending queue with admission, kernel cancellation, a recovery fence, and a descriptor table for `describe`/`call`. Modules keep the Contracts / Module / Aspire.Hosting shape and are rewritten on `Neuron` / `Neuron<TState>`. The Flutter shell keeps its screens minus Application Studio and talks to thin HTTP adapters.

**Tech Stack:** .NET 11 RC1, Orleans 10.3.1 + Orleans.Journaling 10.3.1-alpha.1, Aspire 13.5, Reqnroll over xUnit v3 on Microsoft.Testing.Platform, ModelContextProtocol 2.2, Flutter 3.47.

**Spec:** `docs/superpowers/specs/2026-09-10-communication-model-design.md`

## Global Constraints

- One brain per silo. No owner, principal, actor context or partitioning in the neuron layer.
- Kernel aliases via `nameof`; interface alias `db.v3.neuron`; storage aliases `db.v3.*`. Existing dev storage is wiped, never migrated.
- Signals: type `^[A-Za-z]{1,64}$`, JSON body ≤ 64 KiB. Command args and results ≤ 64 KiB, errors ≤ 4 KiB.
- Bounds: journal windows 512 entries / 512 KiB; `Pending` 256; `ReactedIds` 256; `Dedup` 1024 resolved; latest-per-type 256 types.
- Commands are local and synchronous; snapshots only from reactions; reactions never wait for downstream reactions.
- `[ReadOnly]` allowed on module methods; `[AlwaysInterleave]` kernel-only (`Deliver`, `CancelReaction`, `ReadJournal`, `ReadCommands`, `ReadPendingCount`).
- No `/// <summary>` boilerplate. Small inline comments only where the reason is not obvious. Names must explain themselves.
- Tests: Reqnroll feature per behaviour, one fixture neuron type per fixture. No test that restates the implementation; every scenario proves a spec invariant (1–17) or a user-visible behaviour. Target: fewer, sharper scenarios than core, not more.
- `TreatWarningsAsErrors`, `AnalysisLevel=preview-all` stay on. `dotnet format whitespace --verify-no-changes` must pass.
- Every task ends with: build green, its feature(s) green, `git commit` on `refactor/neurons-only`.
- Never touch anything under `C:\Users\`.

---

## File Structure

Kernel (replaces `src/Kernel/*` except Silo host files that are adapters):

```
src/Kernel/DigitalBrain.Contracts/          grain interface + records (Orleans-visible)
  DigitalBrainNames.cs
  Identity/{NeuronId,SignalId,CorrelationId,CommandId,IdentityPart}.cs
  Signals/{Signal,SignalDelivery,FireOutcome,DeliveryAdmission,SignalJson}.cs
  Synapses/Synapse.cs
  Journals/{JournalKind,JournalRead,JournalSnapshot,JournalTally}.cs
  Commands/{Command,Accepted,CommandRecord,CommandPhase,CommandJournalRead,CommandDescriptor}.cs
  Neurons/{INeuron,NeuronCallTimeouts}.cs
  Descriptors/MethodDescriptor.cs
src/Kernel/DigitalBrain/                    runtime
  IModule.cs
  Hosting/{DigitalBrainRuntime,ModuleAssemblies,ModelPayloadSerialization}.cs
  Neuron/{Neuron,NeuronOfState,PlainNeuron,NeuronRuntime,NeuronConcurrency,INeuronInbox,DrainTelemetry}.cs
  Neuron/{JournalWindow,JournalEntry,NeuronJournals,NeuronSynapses}.cs
  Neuron/{PendingWork,ReactionContext,RetryScheduler}.cs
  Commands/{CommandExecution,CommandJournal,CommandDedup,CommandReconciliation}.cs
  Persistence/{PersistenceFence,BudgetedJournalStorage}.cs
  Context/{CallerContext,OutgoingCallerFilter,CommandLocalityFilter}.cs
  Descriptors/{DescriptorTable,DescriptorRules,NeuronInvoker}.cs
  Serialization/DurableStateJson.cs
src/Kernel/DigitalBrain.Mcp/                client + MCP tools
  {BrainOperations,BrainTools,Requests,SessionPrincipal,DigitalBrainMcpHosting}.cs
src/Kernel/DigitalBrain.Silo/               ASP.NET host + HTTP adapters
  Program.cs, DigitalBrainHost.cs, KernelCors.cs, Auth/BasicAuthGate.cs
  Http/{ChatEndpoints,SurfaceEndpoints,KitEndpoints,GraphEndpoints,SessionStream,StreamWake}.cs
src/Testing/DigitalBrain.Testing/           BrainSimulation, FileJournalStorage, RestartableGrainStorage, FaultingJournalStorage
tests/DigitalBrain.Tests/                   the single test project (Features/*.feature + steps)
```

Deleted: `src/Kernel/DigitalBrain.Scripting`, `src/Kernel/DigitalBrain.Sdk`, `src/Kernel/DigitalBrain.Contracts/Scripting`, `src/Kernel/DigitalBrain/Applications`, `MapApplicationStudio.cs`, `ApplicationAuthoringHttpClient.cs`, `tests/DigitalBrain.Simulation.Tests`, `tests/DigitalBrain.Authoring.Tests`, `tests/DigitalBrain.Substrate.Tests` (replaced by `tests/DigitalBrain.Tests`), `src/Modules/Execution`, Flutter `application_studio.dart`, `application_models.dart`, `application_studio_test.dart`, `.digitalbrain/` store, `artifacts/linux-foundry-check`.

Modules keep `src/Modules/<Name>/{Contracts,<Name>,Aspire.Hosting}` with a vocabulary class, command DTOs deriving from `Command`, and neurons on `Neuron`/`Neuron<TState>`.

---

## Phase A — Kernel

### Task A1: Cut the old kernel, plant the core kernel

**Files:**
- Delete: `src/Kernel/DigitalBrain.Scripting/`, `src/Kernel/DigitalBrain.Sdk/`, `src/Kernel/DigitalBrain.Contracts/**`, `src/Kernel/DigitalBrain/**`, `src/Kernel/DigitalBrain.Mcp/**`, `src/Testing/DigitalBrain.Testing/**`, `src/Testing/DigitalBrain.Testing.E2E/`, `tests/DigitalBrain.Simulation.Tests/`, `tests/DigitalBrain.Authoring.Tests/`, `tests/DigitalBrain.Substrate.Tests/`, `tests/DigitalBrain.AI.Tests/`, `src/Kernel/DigitalBrain.Silo/.digitalbrain/`, `artifacts/linux-foundry-check/`
- Create (copied verbatim from `E:\intochat\digitalbraincore`, same relative paths): `src/Kernel/DigitalBrain.Contracts/**`, `src/Kernel/DigitalBrain/**`, `src/Kernel/DigitalBrain.Mcp/**`, `src/Testing/DigitalBrain.Testing/**`, `tests/DigitalBrain.Tests/**` (features `fire, connect, journal, state, membrane, react, read, mcp` and their steps, `BrainWorld`, `Fixtures`, `SignalJsonFacts`; NOT `agent.feature`, `chat.feature`, `AiSteps`, `ScriptedChatClient` — those belong to the AI module task)
- Modify: `DigitalBrain.slnx` (remove deleted projects, add `tests/DigitalBrain.Tests`), `Directory.Packages.props` (align Journaling/Orleans versions to what the copied kernel needs: `Microsoft.Orleans.*` 10.3.1, Journaling 10.3.1-alpha.1; add `Reqnroll`, `Reqnroll.xunit.v3` if absent), `src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj` (drop Scripting/Sdk refs and the `scripts/**` Content item), `src/Kernel/DigitalBrain.Silo/Program.cs` (temporarily reduce to core's Program: Orleans + `/mcp` + `/health` + `/orleans` + CORS; module endpoints return in Phase C), `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs` (keep Azure clustering wiring, drop `AddDigitalBrainOwner`), `src/Aspire/DigitalBrain.AppHost/AppHost.cs` (remove `DigitalBrain__StartupApplication__Source` and every application/owner reference; keep module `Add*()` calls but comment out modules that no longer compile until their phase lands, one line each)

**Interfaces:**
- Produces: the core `INeuron` (7 methods), `Neuron`, `Neuron<TState>`, `PlainNeuron`, `NeuronRuntime`, `IModule`, `ModuleManifest`, `BrainOperations`, `BrainTools`, `BrainSimulation` exactly as in core HEAD `64792510`.

- [ ] **Step 1:** Delete the listed paths with `git rm -r`. Modules will not compile; that is expected. Unload them from the build by adding `<PropertyGroup><ExcludeFromBuild>` — no: instead remove their `ProjectReference`s from Silo and comment their lines out of `DigitalBrain.slnx` with a `<!-- Phase B -->` marker so the solution builds with kernel + Aspire + Silo only.
- [ ] **Step 2:** Copy the core kernel files listed above. Fix namespaces only where the two repos differ (`DigitalBrain.Abstractions.*`, `DigitalBrain.Core`), keeping core's names.
- [ ] **Step 3:** `dotnet build DigitalBrain.slnx -c Release` green. `dotnet format whitespace DigitalBrain.slnx --verify-no-changes` green.
- [ ] **Step 4:** `dotnet test tests/DigitalBrain.Tests -c Release` green: the eight copied features pass unchanged.
- [ ] **Step 5:** Commit: `refactor!: replace the kernel with the digitalbraincore neuron substrate; delete applications and scripting`.

### Task A2: Durable admission, pending queue, drain, retry, cancellation

**Files:**
- Modify: `Contracts/Neurons/INeuron.cs` (`Deliver` → `Task<DeliveryAdmission>`, add `CancelReaction`), `Contracts/Signals/DeliveryAdmission.cs` (new enum), `DigitalBrain/Neuron/Neuron.cs`, `NeuronRuntime.cs` (register `IDurableQueue<SignalDelivery>` "pending", `IDurableSet<SignalId>` "pending.cancelled", `IDurableList<SignalId>` "reacted" ring), new `Neuron/PendingWork.cs` (queue + cancelled set + reacted ring behind one class: `TryAdmit(delivery) → DeliveryAdmission`, `Peek()`, `Complete(id)`, `Cancel(id) → bool`, `IsCancelled(id)`), new `Neuron/RetryScheduler.cs` (exponential 1 s→60 s, `Interleave=false`, `KeepAlive=true`, armed only while head failed), `Neuron/NeuronConcurrency.cs` (allow `[AlwaysInterleave]` on `INeuron.CancelReaction`), `Mcp/BrainOperations.cs` (`Fire` reports per-target admission), `Mcp/Requests.cs` (`FireResult` gains `Busy` count), `Mcp/BrainTools.cs` (new `cancel` tool).
- Test: `tests/DigitalBrain.Tests/Features/admit.feature`, `cancel.feature`; modify `react.feature` (retry without traffic), `Fixtures.cs` (add `SlowNeuron` whose reaction awaits a `TaskCompletionSource` the test controls and observes its token; `ThrowingNeuron` whose reaction throws N times then succeeds).

**Interfaces:**
- Consumes: A1 kernel.
- Produces: `Task<DeliveryAdmission> Deliver(SignalDelivery, CancellationToken)`; `Task CancelReaction(SignalId)`; `protected SignalId Schedule(Signal, CorrelationId?)` (local, throws `NeuronBusyException`); `Neuron.PersistAsync()` (plain wrapper over `WriteStateAsync(_activation.Token)` for now; fence comes in A4); `FireOutcome(SignalId, CorrelationId, int Delivered, int Busy)`.

- [ ] **Step 1: Write the failing features.**

```gherkin
Feature: Admit
  Deliver accepts into a bounded pending queue. Loss is visible as Busy; a repeated signal id is Duplicate.

  Scenario: A full pending queue refuses admission and the emitter sees it
    Given a running brain
    And a slow "s" whose reaction waits for release
    When "claude" fires 257 "Tick" signals at slow "s"
    Then the last fire reports 1 busy target
    And "s" pending count is 256

  Scenario: The same signal id is admitted once
    Given a running brain
    When "claude" delivers signal id "7f" of type "Note" to plain "p" twice
    Then the second delivery is Duplicate
    And "p" incoming journal contains 1 "Note"

  Scenario: Accepted work survives restart and is reacted to without new traffic
    Given a running brain with file-backed storage
    And a throwing "t" whose reaction fails 2 times then succeeds
    When "claude" fires "Ping" at throwing "t"
    And the silo restarts
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then "t" pending count is 0
```

```gherkin
Feature: Cancel
  CancelReaction reaches a neuron during a long reaction and is observed cooperatively.

  Scenario: Cancelling the reacting entry stops it at its next token check
    Given a running brain
    And a slow "s" whose reaction waits for release
    When "claude" fires "Work" at slow "s"
    And "claude" cancels the pending work on "s"
    Then "s" reaction observed cancellation
    And "s" pending count is 0

  Scenario: Cancelling an id that is not pending writes nothing
    Given a running brain
    When "claude" cancels signal id "00" on plain "p"
    Then "p" journal total recorded is 0
```

- [ ] **Step 2:** Run `dotnet test tests/DigitalBrain.Tests --filter "FullyQualifiedName~Admit|FullyQualifiedName~Cancel"`; expected: fail on missing steps / members.
- [ ] **Step 3:** Implement `PendingWork`, `RetryScheduler`, `Deliver`, `Schedule`, `CancelReaction`, drain rewrite (peek → react → complete → persist; cancelled-head skip; failed-head retry timer; activation re-arm), `FireOutcome.Busy`. `Drain` clears `RequestContext` on entry.
- [ ] **Step 4:** All features green. Existing `react.feature` scenario "Reactions run in journal order" still green.
- [ ] **Step 5:** Commit: `feat(kernel): durable pending queue with admission, kernel cancellation and retry`.

### Task A2b: Retry liveness across a cold restart

**Files:**
- Modify: `DigitalBrain/Neuron/Neuron.cs` (implement `IRemindable`; register reminder `retry` when the head of `Pending` first fails, unregister when the head completes or is cancelled; `ReceiveReminder` only re-arms the drain), `DigitalBrain/Neuron/RetryScheduler.cs` (owns the reminder handle alongside the timer), `Testing/BrainSimulation.cs` (+ new `Testing/FileReminderTable.cs`: an `IReminderTable` backed by a JSON file so reminders survive `RestartSiloAsync`; volatile mode keeps `UseInMemoryReminderService`), `tests/DigitalBrain.Tests/Features/admit.feature` restart scenario (the restart step must NOT touch the neuron; remove the `ReadPendingCount` workaround from the step), `Aspire/DigitalBrain.Aspire` (reminder service already wired for Azure; nothing to add).
- Test: the existing restart scenario in `admit.feature`, unchanged text, now proven without traffic.

**Interfaces:**
- Consumes: A2 `PendingWork`, `RetryScheduler`.
- Produces: nothing new on `INeuron`.

- [ ] **Step 1:** Remove the read-on-restart workaround from the "the silo restarts" step; run the Admit restart scenario; expected: fails (pending count stays 1, no `Pong`).
- [ ] **Step 2:** Implement the reminder registration/unregistration and the file-backed test reminder table. Reminder period = `TimeSpan.FromMinutes(1)` (Orleans minimum); the timer still handles the fast path while the activation lives.
- [ ] **Step 3:** Scenario green three runs in a row; whole project green.
- [ ] **Step 4:** Commit: `feat(kernel): failed pending work survives a cold restart through a reminder`.

### Task A3: Typed commands — journal, dedup, wrapper

**Files:**
- Create: `Contracts/Identity/CommandId.cs` (`readonly record struct` over non-empty Guid, `New()`, `ToString("n")`, `TryParse`), `Contracts/Commands/Command.cs`, `Accepted.cs`, `CommandRecord.cs`, `CommandPhase.cs`, `CommandJournalRead.cs`, `CommandDescriptor.cs` (`record CommandDescriptor(string InterfaceAlias, string MethodAlias)`), `DigitalBrain/Commands/CommandJournal.cs` (third `JournalWindow` specialised for `CommandRecord`; `EarliestRetained`, `Gap`, `CommittedSequence`), `CommandDedup.cs` (`IDurableDictionary<CommandId, CommandOutcome>`, 1024 resolved, unresolved never evicted, `Busy` when full of unresolved; match on caller+interface+method+sha256(args)), `CommandExecution.cs` (the wrapper: steps 1–4 from spec §4), `Neuron/ReactionContext.cs` (`abstract record ReactionContext; sealed record DeliveryReaction(SignalDelivery); sealed record CommandReaction(CommandId, SignalId Work)`), exceptions `NeuronBusyException`, `CommandOutcomeUnknownException`, `CommandRejectedException`.
- Modify: `INeuron.cs` (add `ReadCommands`), `Neuron.cs` (`ExecuteCommandAsync`, `ReactionContext` property, `FireAsync`/`SaveAsync` throw inside a command), `NeuronOfState.cs`, `JournalRead` (+`EarliestRetained`, `Gap`), `NeuronRuntime.cs` (register "commands" window + "dedup"), `Mcp/Requests.cs` + `BrainOperations.cs` (`read` gains `what: "commands"`).
- Test: `tests/DigitalBrain.Tests/Features/command.feature`, `Fixtures.cs` (`CounterNeuron : Neuron<CounterState>, ICounter` with `[Alias("test.counter")] interface ICounter : INeuron { [Alias("add")] Task<Accepted<int>> Add(AddCount cmd); [ReadOnly, Alias("total")] Task<int> ReadTotal(); }` where `Add` schedules `Counted` and the reaction saves the snapshot).

**Interfaces:**
- Produces: `protected Task<TResult> ExecuteCommandAsync<TArgs,TResult>(CommandDescriptor, TArgs, JsonTypeInfo<TArgs>, JsonTypeInfo<TResult>, Func<TArgs,TResult>) where TArgs : Command`; `Task<CommandJournalRead> ReadCommands(long afterSequence)`; `abstract record Command(CommandId Id, long? ExpectedVersion = null)`; `sealed record Accepted<TReceipt>(TReceipt Receipt, SignalId Work)`.

- [ ] **Step 1: Write the failing feature.**

```gherkin
Feature: Command
  A typed command is local, synchronous, audited, and deduplicated.

  Scenario: An accepted command schedules work and the reaction saves the snapshot
    Given a running brain
    When "claude" adds 3 to counter "c" with command id "a1"
    Then the add is accepted with a work id
    And "c" commands journal shows "a1" as Attempted then Completed
    And "claude" waits up to 5 seconds until counter "c" total is 3

  Scenario: A resolved command id returns the stored outcome without executing
    Given a running brain
    When "claude" adds 3 to counter "c" with command id "a1"
    And "claude" adds 3 to counter "c" with command id "a1"
    Then counter "c" executed 1 time
    And both adds return the same work id

  Scenario: A command id reused with different arguments is rejected
    Given a running brain
    When "claude" adds 3 to counter "c" with command id "a1"
    And "claude" adds 4 to counter "c" with command id "a1"
    Then the second add fails with "command id reused"
    And "c" commands journal shows one Rejected record

  Scenario: Oversize arguments are rejected before execution
    Given a running brain
    When "claude" adds a 70000-byte note to counter "c" with command id "big"
    Then the add fails with the membrane message
    And counter "c" executed 0 times

  Scenario: Saving a snapshot inside a command is refused
    Given a running brain
    When "claude" invokes the misbehaving save on counter "c"
    Then it fails with "save state from a reaction, not a command"

  Scenario: A crash after Attempted recovers as Unknown and a retry re-executes once
    Given a running brain with file-backed storage
    And counter "c" crashes after recording Attempted
    When "claude" adds 3 to counter "c" with command id "x9" and the call fails
    And the silo restarts
    Then "c" commands journal shows "x9" as Attempted then Unknown
    When "claude" adds 3 to counter "c" with command id "x9"
    Then "c" commands journal shows "x9" incarnation 2 as Attempted then Completed
    And "claude" waits up to 5 seconds until counter "c" total is 3
```

- [ ] **Step 2:** Run the feature; expected: fails on missing types.
- [ ] **Step 3:** Implement. Canonical args hash = SHA-256 over the STJ serialization with the assembly's source-generated context. Reconciliation on activation: `Dedup` entries still `Attempted` with no terminal record → append `Unknown`.
- [ ] **Step 4:** Feature green; whole test project green.
- [ ] **Step 5:** Commit: `feat(kernel): typed commands with an audited command journal and bounded dedup`.

### Task A4: Persistence fence, reconciliation, storage budget, caller context

**Files:**
- Create: `DigitalBrain/Persistence/PersistenceFence.cs` (the `PersistAsync` / `RecoverAsync` logic from spec §5, `_faulted` check at every `INeuron` method entry via a shared `Guard()` call, `NeuronRecoveringException`, `NeuronPersistenceException`), `Persistence/BudgetedJournalStorage.cs` (decorator over the journal storage provider interface applying a 120 s `CancellationTokenSource` per append/replace/read and awaiting settlement), `Commands/CommandReconciliation.cs` (shared by activation and recovery), `Context/CallerContext.cs` (`db.caller`, `db.correlation`, `db.causation` keys, scoped set/restore), `Context/OutgoingCallerFilter.cs` (`IOutgoingGrainCallFilter`: stamp `SourceId`), `Context/CommandLocalityFilter.cs` (`IOutgoingGrainCallFilter`: reject calls whose source activation's `ReactionContext` is a `CommandReaction`).
- Modify: `Neuron.cs` (use `PersistAsync` everywhere; reconciliation in `OnActivateAsync`; `Deliver`/wrapper capture caller context first), `Aspire/DigitalBrain.Aspire/AzureOrleansJournalHosting.cs` (wrap the storage with `BudgetedJournalStorage`), `Testing/BrainSimulation.cs` + new `Testing/FaultingJournalStorage.cs` (storage that fails the next N writes, or throws `OperationCanceledException` on demand, controllable from steps).
- Test: `tests/DigitalBrain.Tests/Features/recovery.feature`, `lineage.feature`.

- [ ] **Step 1: Write the failing features.**

```gherkin
Feature: Recovery
  A neuron never serves state that storage does not hold.

  Scenario: A failed write fences the activation until revert completes
    Given a running brain with faulting storage
    And storage fails the next write
    When "claude" connects "claude" to plain "p" for "Note" and the call fails
    Then reading synapses of "claude" throws NeuronRecovering or shows no "Note" synapse
    And after storage recovers, "claude" synapses do not include "Note" to "p"

  Scenario: Storage cancellation with a live activation is a fault, not a shutdown
    Given a running brain with faulting storage
    And storage cancels the next write
    When "claude" fires "Note" at plain "p" and the call fails
    Then "p" state does not contain "Note"

  Scenario: Reconciliation runs after in-place recovery
    Given a running brain with faulting storage
    And storage fails the write after counter "c" records Attempted
    When "claude" adds 3 to counter "c" with command id "r1" and the call fails
    Then "c" commands journal shows "r1" as Attempted then Unknown
    When "claude" adds 3 to counter "c" with command id "r1"
    Then "c" commands journal shows "r1" incarnation 2 as Attempted then Completed
```

```gherkin
Feature: Lineage
  Caller, correlation and causation survive hops.

  Scenario: A signal fired by the reaction a command scheduled carries the command as causation
    Given a running brain
    And "claude" is connected to counter "c" for "Counted"
    When "claude" adds 1 to counter "c" with command id "l1"
    And "claude" waits up to 5 seconds for an incoming "Counted"
    Then the latest "claude" incoming entry causation equals the work id of "l1"
    And "c" commands journal record "l1" causation is empty

  Scenario: A grain call from inside a command is refused
    Given a running brain
    When "claude" invokes the misbehaving call-out on counter "c"
    Then it fails with "commands are local"
```

- [ ] **Step 2:** Run; expected failures on missing storage fixture and filters.
- [ ] **Step 3:** Implement. `Guard()` is the first statement of every `INeuron` implementation method and of `ExecuteCommandAsync`.
- [ ] **Step 4:** Green. Whole project green.
- [ ] **Step 5:** Commit: `feat(kernel): persistence fence with in-place recovery, storage budget, caller lineage`.

### Task A5: Descriptor table, `describe` / `call`, committed reads

**Files:**
- Create: `Contracts/Descriptors/MethodDescriptor.cs`, `DigitalBrain/Descriptors/DescriptorRules.cs` (the table from spec §7, with a `Validate(Type grainClass)` that throws naming interface+method), `DescriptorTable.cs` (built at silo start from all `[GrainType]` classes implementing `INeuron`; `IReadOnlyList<MethodDescriptor> For(GrainType)`, `MethodDescriptor Get(string interfaceAlias, string methodAlias)`), `NeuronInvoker.cs` (`INeuronInvoker` with `Describe(NeuronId)`, `Describe(interfaceAlias, methodAlias)`, `InvokeAsync(NeuronId, interfaceAlias, methodAlias, JsonElement args, ct)`; cached delegates via `GetGrain<TInterface>`; interface-implementation check).
- Modify: `Mcp/BrainTools.cs` (+`describe`, `call`), `Mcp/BrainOperations.cs`, `Mcp/Requests.cs` (`DescribeRequest`, `CallRequest`), `Neuron.cs` (`ReadJournal`/`ReadCommands` serve ≤ `CommittedSequence`; `Gap` computed from the same view), `DigitalBrainRuntime.cs` (build the table, fail fast).
- Test: `tests/DigitalBrain.Tests/Features/describe.feature`; modify `mcp.feature` (tool count 7), `journal.feature` (gap reporting).

- [ ] **Step 1: Write the failing feature.**

```gherkin
Feature: Describe
  An agent reads a neuron's interfaces and calls typed methods through the same table.

  Scenario: Describe lists a module interface with its methods and schemas
    Given a running brain
    When "claude" describes counter "c"
    Then the description lists interface "test.counter" with methods "add" and "total"
    And method "add" args schema requires "id" and "count"
    And method "total" is read-only

  Scenario: Call invokes a typed method with JSON arguments
    Given a running brain
    When "claude" calls "test.counter" "add" on counter "c" with {"id":"c1","count":2}
    Then the call returns a receipt and a work id
    And "claude" waits up to 5 seconds until counter "c" total is 2

  Scenario: Calling an interface the neuron does not implement names the valid ones
    Given a running brain
    When "claude" calls "test.counter" "add" on plain "p" with {"id":"c2","count":1}
    Then the call fails naming "db.v3.neuron"

  Scenario: A read past the retained window reports a gap
    Given a running brain
    When "claude" fires 600 "Tick" signals at plain "p"
    And "claude" reads "p" incoming journal after sequence 0
    Then the read reports a gap and an earliest retained sequence above 1
```

- [ ] **Step 2:** Run; expected failures.
- [ ] **Step 3:** Implement. A descriptor violation in any loaded grain class fails `DigitalBrainRuntime.Add` with the interface and method named.
- [ ] **Step 4:** Green.
- [ ] **Step 5:** Commit: `feat(kernel): descriptor table with describe and call; committed journal reads with gaps`.

### Task A6: Kernel review gate

- [ ] **Step 1:** Fresh reviewer (Codex `review` mode plus a Claude review subagent) over every file under `src/Kernel`, `src/Testing`, `tests/DigitalBrain.Tests`: naming, dead code, comment noise, allocation in hot paths, invariant coverage 1–17, any `[AlwaysInterleave]` outside the four kernel uses.
- [ ] **Step 2:** Fix everything found. Re-run whole test project.
- [ ] **Step 3:** Commit: `refactor(kernel): review pass`.

---

## Phase B — Modules (parallel after A6)

Each module task follows the same shape: rewrite Contracts (interface extends `INeuron`, DTOs derive from `Command`, vocabulary class with body records + STJ context), rewrite the Module (neurons on `Neuron`/`Neuron<TState>`; commands via `ExecuteCommandAsync`; reactions for I/O), keep Aspire.Hosting projection, re-enable in `DigitalBrain.slnx`, `Silo.csproj`, `AppHost.cs`, `Container.pubxml`; one feature file per module in `tests/DigitalBrain.Tests/Features/<module>.feature`; commit `feat(<module>): port to the neuron model`.

Order: B0 then B1 run serially (B2's chat fires `Turn` at B1's agent neuron). B2–B5 run in parallel, each implementer in its own git worktree on `refactor/neurons-only-b<N>`, merged back by the controller after its review; shared files (`DigitalBrain.slnx`, `Silo.csproj`, `AppHost.cs`, `Container.pubxml`, the tests csproj) are edited only in the lines the module owns. Module Contracts projects enable `GenerateDocumentationFile` and carry one-line `<summary>` on grain interface methods and command DTOs: those are the agent's tool descriptions, not boilerplate.

### Task B0: Dissolve Product.Contracts and stage the module build order

**Files:**
- Delete: `src/Product/DigitalBrain.Product.Contracts/**` (11 files). Its `Identity/CommandId.cs` is superseded by the kernel's `DigitalBrain.Contracts/Identity/CommandId.cs`; `Conversations/UserMessaged.cs`, `Interactions/*` (`AgentTurnContext`, `ITrustedUserCommandHandler`, `IUntrustedContentScreen`, `IUserActionContinuation`, `IUserActionSource`, `SetupContinuation`, `SpecialistContinuation`, `UserActionRequest`) and `Presentation/NeuronPresentation.cs` are product-interaction contracts from the applications era.
- Create: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Interactions/UntrustedContentScreen.cs` — ONLY if a Phase B module still needs a shared "screen external content before the model sees it" seam; carry `IUntrustedContentScreen` there unchanged in shape (interface + result record). Everything else in Product.Contracts is deleted, not moved: user actions, continuations and trusted-command handlers are what commands and signals now express.
- Modify: every module csproj that references `DigitalBrain.Product.Contracts` (AI, AI.Contracts, Excel, Google, Memory.Contracts, Salesforce, Time, Time.Contracts, UI, UI.Contracts, Execution ×2 — Execution is deleted in B6, leave its csproj untouched) → remove the reference; where a module used `IUntrustedContentScreen`, point it at the UI.Contracts copy (a ProjectReference to UI.Contracts is acceptable for AI; if it would create a cycle, the seam moves to AI.Contracts instead — decide and say which).
- Modify: `DigitalBrain.slnx` — remove the Product block; nothing else re-enabled here.

**Interfaces:**
- Consumes: kernel `CommandId`.
- Produces: no `Product` namespace anywhere; modules compile-time references cleaned for their own tasks.

- [ ] **Step 1:** `grep -rn "DigitalBrain.Product" src tests` — list every usage; decide per file: delete the using, or repoint to the UI.Contracts seam.
- [ ] **Step 2:** Delete the project; update csproj references and the slnx.
- [ ] **Step 3:** `dotnet build DigitalBrain.slnx -c Release` green (modules are still commented out, so this proves only that nothing enabled referenced Product); `dotnet test tests/DigitalBrain.Tests -c Release` green.
- [ ] **Step 4:** Commit: `refactor!: dissolve Product.Contracts; CommandId lives in the kernel`.

### Task B1: AI module (agent + chat neurons)

**Files:** copy `AgentNeuron.cs`, `ChatNeuron.cs`, `AIModule.cs`, `BrainTools` (AI functions), `Providers`, `Bodies`, states, `AIVocabulary` from core's `src/Modules/AI`; port this repo's provider catalogue (OpenAI, Anthropic, Ollama, Foundry transcription) into `Providers/` behind keyed `IChatClient`; keep `Voice/*TranscriptionService` as plain services; delete `AgentRequest`, `ApplicationAgentExtensions`, `Sdk/`. Tests: `agent.feature`, `chat.feature` from core (copied, plus `AiSteps`, `ScriptedChatClient`). Agent tools = the seven kernel operations as `AIFunction`s (extend core's `BrainTools.For` with `describe`, `call`, `cancel`) plus typed functions from descriptors named in `Instruct.tools`.

### Task B2: UI module — chat surface

**Files:** `Contracts/Chat/IChat.cs` (`[Alias("ui.chat")]`: `Send(SendMessage) → Accepted<TurnId>`, `[ReadOnly] ReadTranscript(ReadTranscript) → ChatTranscript`, `[ReadOnly] ReadTurn(ReadTurn) → TurnView`), `UIVocabulary` (`TurnRequested`, `Responded`, `TurnFailed`, `ActivityChanged`, `SurfaceChanged`), `Chat/ChatNeuron.cs : Neuron<ChatState>` (reaction to `TurnRequested` creates the turn, imports `TurnContext` per spec §10, fires `Turn` at the configured agent neuron, reacts to `Said`/`Reply` by appending the transcript, saving, and firing `Responded` along synapses), `Chat/TurnContext.cs` (ported from Execution: path, digest with the existing recipe from `ExecutionContextEntity.cs:100`, schema hash, payload/blob ref, precedence, related turns; last 32 inline). Delete `UserMessagesNeuron`, `IComposer`, `ChatTurnWorker`, `AgentTurnWorker`, `ChatExecutionFocus`, `IChatKernel`, `WorkspaceChatScenarioDriver`, `ApplicationChatPorts`. Feature: `uichat.feature` (send → transcript shows user turn; agent reply lands in transcript and `Responded` reaches the session; cancel via work id stops a slow scripted agent).

### Task B3: UI module — surfaces, renderer, kit entities

**Files:** `SurfaceNeuron : Neuron<SurfaceState>, ISurface` (`Open(OpenSurface)`, `Activate(ActivateControl)`, `[ReadOnly] Read()`), `ChartNeuron`, `GraphNeuron`, `ImageNeuron`, `TranscriptNeuron`, `WorkspaceIndexNeuron` each `Neuron<TState>` with `Put(...)` command and `[ReadOnly] Read()`; `UIRenderer` folded into `SurfaceNeuron` reactions; `ActivitiesNeuron` + `ActivitySourceNeuron` moved from the kernel into the UI module as `Neuron<ActivityState>`; delete `Entity<TState>`, `IEntity`, `EntityId`, `[ApplicationJsonContract]`. Feature: `surface.feature`.

### Task B4: Memory, Time, Excel

**Files:** `Memory`: `IMemory : INeuron` with `Remember(Remember) → Accepted<MemoryId>`, `[ReadOnly] Recall(Recall) → RecallResult`; vector store call in the reaction. `Time`: `ITimer : INeuron` with `Schedule(ScheduleTimer) → Accepted<TimerId>`, `Cancel` via `CancelReaction`; the due signal is fired by an Orleans reminder-driven reaction (`Neuron` may not register timers; the Time module owns one reminder grain per timer neuron). `Excel`: `ISpreadsheet : INeuron` with `Apply(ApplySheetEdit)`, `[ReadOnly] Read(ReadRange)`. Features: `memory.feature`, `time.feature`, `excel.feature`, each two or three scenarios.

### Task B5: Google, Salesforce, Microsoft

**Files:** each integration neuron becomes `Neuron<TState>` with connection state; webhook ingress = an HTTP adapter in Silo that calls `Deliver` on the integration neuron's session (`NeuronId("session", "<integration>")`) after signature verification; OAuth callbacks stay module HTTP surfaces (`UseModuleHttpSurfaces`) but write through a typed `Connect(ConnectAccount)` command. Features: `integrations.feature` with one scenario per module proving connect + one inbound signal, using the existing fake providers.

### Task B6: Execution deletion

- [ ] Verify `TurnContext` digest matches `ExecutionContextEntity` recipe on the same input (one xUnit fact). Verify blob round-trip. `git rm -r src/Modules/Execution`; remove from slnx, Silo, AppHost, pubxml. Commit `refactor!: remove the Execution module; turn context lives in chat`.

### Task B7: Module review gate

- [ ] Same as A6 over `src/Modules/**` and the module features. Additionally check every module for: no `[AlwaysInterleave]`, every mutating DTO derives from `Command`, no grain call inside a command, no `SaveAsync` outside reactions, vocabulary strings validated by the membrane regex.

---

## Phase C — Silo HTTP edge and Flutter shell

### Task C1: HTTP adapters

**Files:** `Silo/Http/ChatEndpoints.cs` (`POST /chats/{name}/send` → `IChat.Send`; `POST /chats/{name}/turns/{work}/cancel` → `CancelReaction`; `GET /chats/{name}/transcript`), `SurfaceEndpoints.cs`, `KitEndpoints.cs` (the six kit GETs over typed reads), `GraphEndpoints.cs` (brain graph over `ReadSynapses` + journals), `SessionStream.cs` (`GET /sessions/{login}/events`: long-poll over `ReadJournal(Incoming, after)`, `reset` event with the typed projection when `Gap`), `StreamWake.cs` (Orleans broadcast channel published after each `Deliver` persist; consumed by `SessionStream` to end a long-poll early), `Auth/BasicAuthGate.cs` (kept; maps the login to `NeuronId.Plain(login)`), `MapChatVoice` kept over the AI transcription service. `Program.cs` maps these plus `/mcp`, `/health`, `/orleans`. Tests: `http.feature` using `WebApplicationFactory` (send, transcript, stream reset, cancel).

### Task C2: Flutter shell

**Files:** delete `application_studio.dart`, `application_models.dart`, `application_studio_test.dart`; prune `/applications/*` methods from `core/lib/src/ui_client.dart`; repoint chat send/cancel/transcript/stream calls to C1 paths; remove studio routes from `brain_chat_app.dart`, `brain_workspace.dart`, `graph_home_screen.dart`, `digitalbrain_flutter.dart`. `flutter analyze`, `dart format`, existing shell tests green, `flutter build web --release`. Commit `feat(shell): drop Application Studio; use the neuron HTTP edge`.

### Task C3: Deploy and container

**Files:** `Container.pubxml` module list = AI, Memory, Time, Excel, Google, Salesforce, Microsoft, UI; drop the `scripts/**` content; keep the Foundry Linux exclusion. `deploy.yml`: add the image smoke step (Azurite + image on the runner, wait for `/health`, 90 s) between publish and roll. Local proof: `docker run` the published image against Azurite as in the 2026-09-09 diagnosis and see `/health` 200.

---

## Phase D — Verification, review, PR

### Task D1: Real-user run through Aspire and MCP

- [ ] `aspire run` the AppHost; use the Aspire MCP tools (`list_resources`, `list_console_logs`, `list_structured_logs`) to confirm every resource healthy and no error logs.
- [ ] Through the brain MCP (`/mcp`): `describe` the chat neuron; `call` `ui.chat.send` twice; `read` the session inbox and see `Responded`; `cancel` a slow turn; `connect` a plain neuron to the chat for `Responded` and see fan-out; `read` `commands` on the chat and check phases.
- [ ] Through the Flutter web shell: send a message, see the reply, open a surface, activate a control, watch the graph update. Record what was exercised in `docs/superpowers/plans/2026-09-10-neurons-only-migration.md` under a "Verified" heading with dates.

### Task D2: Full review gates

- [ ] Codex `review` over the whole diff `master..refactor/neurons-only`; Claude review subagents per area (kernel, modules, silo, shell, tests) with the brief: names, dead code, comment noise, duplicated logic, any regression in the spec's rules; every finding fixed or explicitly rejected with a reason in the PR body.
- [ ] `dotnet format`, `dotnet build`, `dotnet test`, `flutter analyze`, `flutter test`, `flutter build web` all green in one run.

### Task D3: PR

- [ ] `git push -u origin refactor/neurons-only`; `gh pr create` to master titled `refactor!: neurons-only core with typed commands; delete applications and scripting`, body: the spec summary, the deletion table, the verified-run record, review findings and their resolution, attribution lines.
