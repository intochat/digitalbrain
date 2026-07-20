# Foundation PoC Implementation Plan

> **For agentic workers:** Execute this plan with the repository's `tdd` skill. Use
> `subagent-driven-development` only when the user explicitly requests delegated execution;
> otherwise execute inline, one green slice at a time.

**Goal:** Prove that DigitalBrain can durably execute one useful cross-module task through Tasks,
AI, Google, Salesforce, and Time without leaking provider, MCP, or orchestration mechanics into the
Kernel or public behavior vocabulary.

**Architecture:** Typed neuron interfaces are the public programming language; synapses are the
durable causal substrate. Orleans owns neuron identity, turns, journals, and recovery. Microsoft
Agent Framework (MAF) owns agents and orchestration. Official MCP servers remain module-private
adapters behind semantic Google and Salesforce neuron identities. Each slice adds one public
behavior and its proof; no horizontal package scaffolding is allowed ahead of a failing consumer.

**Tech stack:** .NET 10, C# latest, Orleans 10.2.2-rc.2, Orleans Journaling
10.2.2-rc.2.alpha.1, Aspire 13.4.6, Microsoft.Extensions.AI 10.8.0, Microsoft Agent Framework
1.13.0, Model Context Protocol .NET SDK 1.4.1, xUnit v3, Reqnroll.

**Plan approval freezes:** the public seams in “Frozen PoC contracts,” the neutral runtime seam in
“Capability-tool boundary,” and the vertical order below. A new public abstraction, provider
credential shape, or workflow engine discovered during execution is a stop-and-record event, not
permission to improvise.

---

## Scope lock

The executable proof is:

```text
Create durable Task
  -> read Gmail through the official MCP server
  -> ask ILlama32 and IGpt56 independently
  -> reconcile their answers through a MAF group
  -> restart the silo between Lockstep supersteps
  -> request human approval for an exact Salesforce mutation
  -> update one Salesforce record through the official MCP server
  -> schedule one follow-up Countdown and one one-shot Reminder
  -> complete with a typed Result, evidence references, and causal journals
```

The framework implementation must support a deterministic automated version with fake
`IChatClient` and fake MCP transports. A credentialed live smoke test is optional and never part of
the root gate.

The PoC does not include Memory, Flutter, recurring or calendar schedules, runtime behavior
installation, a broad integration facade, model tiers, routing, balancing, fallback, cost policy,
provider descriptors, or a second agent/workflow engine.

## Fixed dependency direction

```text
DigitalBrain.Abstractions
  <- DigitalBrain.Kernel
  <- module runtimes

DigitalBrain.Modules.Tasks.Contracts -> DigitalBrain.Abstractions
DigitalBrain.Modules.Tasks           -> Tasks.Contracts + Kernel

DigitalBrain.Modules.AI.Contracts    -> Tasks.Contracts + MEAI abstractions
DigitalBrain.Modules.AI              -> AI.Contracts + Kernel + MAF

DigitalBrain.Capabilities            -> DigitalBrain.Abstractions
Google/Salesforce runtimes           -> own Contracts + Kernel + Capabilities + MCP SDK
AI runtime                           -> Capabilities

Time.Contracts                       -> DigitalBrain.Abstractions
Time runtime                         -> Time.Contracts + Kernel
```

`DigitalBrain.Capabilities` is module-author infrastructure. It is not a behavior contract, a
natural-language registry entry, or a model-visible raw invocation API. Contract packages, clients,
and behavior compilation must not reference it.

## Frozen PoC contracts

### Shared command identity

Add `DigitalBrain.Abstractions.CommandId` as a non-empty `Guid` value type with alias
`db.command-id`. It identifies an idempotent domain command; it is not an RPC delivery identifier.

### AI

```csharp
namespace DigitalBrain.AI;

public interface ILLM : INeuron
{
    Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages);
}

public interface IAgent : INeuron
{
    Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages);
}

public interface IGroupChat : IAgent, IWorker;
```

Callers never pass `ChatOptions`. A concrete LLM owns its keyed `IChatClient`; an Agent owns its
instructions and tools. `ILLM` never inherits `IAgent`.

The consumed runtime base is:

```csharp
public abstract class Agent : Neuron, IAgent
{
    protected Agent(ILLM model, params INeuron[] capabilities);

    protected abstract string Instructions { get; }

    public Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages);
}
```

An application may compute `Instructions` dynamically from its own typed state and constructor
dependencies. Capabilities passed to the base are stable typed neuron identities, not tool names or
MCP clients.

The PoC implements only the orchestration bases it consumes:

```csharp
public abstract class Concurrent : Neuron, IAgent;
public abstract class GroupChat : Neuron, IGroupChat;

public abstract record Participant(NeuronId Id);
public sealed record Participant<TNeuron>(NeuronId Id) : Participant(Id)
    where TNeuron : INeuron;
```

Concrete orchestrations return typed `Participant<TNeuron>` entries. The runtime accepts
`TNeuron : IAgent` or `TNeuron : ILLM` and rejects every other contract. Sequential, Handoff, and
Magentic remain ratified vocabulary but are not implemented until consumed.

### Tasks

```csharp
namespace DigitalBrain.Tasks;

public interface ITask : INeuron
{
    Task<TaskSnapshot> StartAsync(StartTask command);
    Task<TaskSnapshot> CancelAsync(CancelTask command);
    Task<TaskSnapshot> ReadAsync();
}

public interface IWorker : INeuron
{
    Task AcceptAsync(AttemptRequest request);
    Task ContinueAsync(AttemptCursor cursor);
    Task CancelAsync(AttemptCursor cursor);
}

public abstract record Goal;
public abstract record Result;
public abstract record Failure;
public abstract record TaskBlocker;

public readonly record struct AttemptId(Guid Value);
public readonly record struct BlockerId(Guid Value);
public readonly record struct FactReference(NeuronId Source, SynapseId Fact);

public sealed record TaskPolicy(
    int MaximumAttempts,
    TimeSpan RetryDelay,
    DateTimeOffset? Deadline);

public sealed record StartTask(
    CommandId CommandId,
    Goal Goal,
    NeuronId Worker,
    TaskPolicy Policy,
    NeuronId? RetryOf = null);

public sealed record CancelTask(CommandId CommandId, long ExpectedRevision);

public sealed record AttemptRequest(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision,
    Goal Goal);

public sealed record AttemptCursor(
    NeuronId Task,
    NeuronId Worker,
    AttemptId Attempt,
    long Revision);

public sealed record TaskSnapshot(
    Goal Goal,
    NeuronId Worker,
    TaskPolicy Policy,
    TaskState State,
    long Revision,
    AttemptId? ActiveAttempt,
    TaskBlocker? Blocker,
    Result? Result,
    Failure? Failure,
    IReadOnlyList<FactReference> Evidence,
    NeuronId? RetryOf);
```

`ReadAsync` before the first successful `StartAsync` rejects because no Task exists yet. The Task
state vocabulary is `Pending`, `Running`, `Waiting`, `Cancelling`, `Succeeded`, `Failed`, and
`Cancelled`.

Attempt facts derive from one serializable `AttemptFact` carrying Task, Worker, Attempt, and
Revision:

```text
AttemptAccepted
AttemptAdvanced
AttemptProgressed
AttemptWaiting
AttemptSucceeded
AttemptFailed
AttemptCancelled
AttemptOutcomeUncertain
```

Each blocker derives from `TaskBlocker` and carries a non-empty `BlockerId`; the PoC blockers are
`InputRequired`, `ApprovalRequired`, `DependencyPending`, `RetryScheduled`, and
`OutcomeUncertain`. Details remain in typed worker/integration facts rather than a payload bag.
`AttemptSucceeded` carries one typed Result and evidence references.
`AttemptFailed` carries one typed Failure and retryability. No Tasks type contains an AI prompt,
MAF type, arbitrary JSON, metadata dictionary, or generic event string.

### Integration capability roots

```csharp
namespace DigitalBrain.Google;

public interface IGmail : INeuron;
```

```csharp
namespace DigitalBrain.Salesforce;

public interface ISalesforce : INeuron;
```

These marker interfaces identify addressable semantic capability neurons. They deliberately do not
freeze an MCP-shaped CRUD facade. Their complete pinned/authenticated/granted MCP catalogs are
private runtime data. A deterministic high-level request is added to a public integration contract
only when a real non-agent caller requires it.

### Time

The PoC freezes only one-shot schedules:

```csharp
namespace DigitalBrain.Time;

public interface ICountdown : INeuron
{
    Task<CountdownSnapshot> StartAsync(StartCountdown command);
    Task<CountdownSnapshot> RescheduleAsync(RescheduleCountdown command);
    Task<CountdownSnapshot> CancelAsync(CancelCountdown command);
    Task<CountdownSnapshot> RestartAsync(RestartCountdown command);
    Task<CountdownSnapshot> ReadAsync();
}

public interface IReminder : INeuron
{
    Task<ReminderSnapshot> ScheduleAsync(ScheduleReminder command);
    Task<ReminderSnapshot> RescheduleAsync(RescheduleReminder command);
    Task<ReminderSnapshot> CancelAsync(CancelReminder command);
    Task<ReminderSnapshot> RestartAsync(RestartReminder command);
    Task<ReminderSnapshot> ReadAsync();
}
```

Initial commands bind one same-owner `Destination`:

```csharp
public sealed record StartCountdown(CommandId CommandId, TimeSpan Duration, NeuronId Destination);
public sealed record RescheduleCountdown(
    CommandId CommandId,
    long ExpectedRevision,
    TimeSpan Duration);
public sealed record CancelCountdown(CommandId CommandId, long ExpectedRevision);
public sealed record RestartCountdown(CommandId CommandId, TimeSpan Duration);

public sealed record ScheduleReminder(
    CommandId CommandId,
    DateTimeOffset DueAt,
    NeuronId Destination);
public sealed record RescheduleReminder(
    CommandId CommandId,
    long ExpectedRevision,
    DateTimeOffset DueAt);
public sealed record CancelReminder(CommandId CommandId, long ExpectedRevision);
public sealed record RestartReminder(CommandId CommandId, DateTimeOffset DueAt);

public enum ScheduleState
{
    Unscheduled,
    Scheduled,
    Elapsed,
    Cancelled,
}

public enum ScheduleResolution
{
    OnTime,
    Recovered,
}

public sealed record CountdownSnapshot(
    ScheduleState State,
    long Generation,
    long Revision,
    NeuronId? Destination,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? DueAt,
    TimeSpan? Duration);

public sealed record ReminderSnapshot(
    ScheduleState State,
    long Generation,
    long Revision,
    NeuronId? Destination,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? DueAt);
```

Restart retains the destination and creates a new generation; changing destination requires a new
neuron identity. `CountdownElapsed` and `ReminderDue` carry schedule neuron, generation, revision,
destination, scheduled time, observed time, and `ScheduleResolution`—never an arbitrary payload.

Recurring `IReminder` commands and calendar recurrence records remain deferred. They are added only
with a real recurring PoC and the already-ratified overdue/DST semantics.

## Capability-tool boundary

The neutral `src/DigitalBrain.Capabilities` package exists because Google, Salesforce, and a
deterministic fake are three independent adapters. Its public CLR surface is hidden from IntelliSense
with `EditorBrowsable(Never)` and guarded from contract/behavior references.

It carries provider-neutral immutable values:

```csharp
public enum CapabilityOperation
{
    Read,
    Mutation,
    Unknown,
}

public sealed record CapabilityTool(
    string Name,
    string Description,
    JsonElement InputSchema,
    CapabilityOperation Operation);

public sealed record CapabilityToolCatalog(
    NeuronId Capability,
    string Revision,
    IReadOnlyList<CapabilityTool> Tools);

public sealed record CapabilityToolInvocation(
    NeuronId Caller,
    NeuronId Capability,
    string CatalogRevision,
    string Tool,
    JsonElement Arguments,
    CommandId? CommandId,
    string Fingerprint,
    CapabilityApproval? Approval);

public sealed record CapabilityApproval(
    Guid ApprovalId,
    OwnerId ApprovedBy,
    string Fingerprint,
    DateTimeOffset ApprovedAt);

public sealed record CapabilityToolResult(JsonElement Content);

public interface ICapabilityToolSource
{
    Type CapabilityContract { get; }

    ValueTask<CapabilityToolCatalog> ReadCatalogAsync(
        NeuronId capability,
        CancellationToken cancellationToken);

    ValueTask<CapabilityToolResult> InvokeAsync(
        CapabilityToolInvocation invocation,
        CancellationToken cancellationToken);
}
```

One source is registered per semantic contract, not per owner or neuron instance. AI matches the
typed capability proxy to `CapabilityContract`, then passes its exact owner-bound `NeuronId` into the
source. This interface is callable only by AI runtime infrastructure registered in the silo. It is
excluded from behavior references and generated semantic discovery. AI converts a selected
`CapabilityTool` to an exact MAF/MEAI `AIFunction`; the model never receives
`ICapabilityToolSource.InvokeAsync`, an MCP client, or a generic raw-invoke tool.

Every source implementation routes invocation through the module-private method on the semantic
capability neuron. For normal neuron calls, Kernel filters commit and propagate
`CapabilityRequested`. For a private off-turn MAF runner, the owning orchestration neuron first
commits the request and issues a revision-fenced, one-use invocation lease; the private runner then
propagates that delivery through `RequestContext`. The integration neuron journals the same incoming
request before its module-private method runs. This preserves the approved causal rail without
pretending the runner is a public neuron.

## TDD and commit protocol

For every task:

1. Record `git rev-parse HEAD` and `git status --short`. If HEAD or an unrelated file changed, stop
   and surface it.
2. Add one public behavior test. Run the smallest owning test project and quote the expected failure.
3. Add only enough production code to pass that proof.
4. Run the owning test project.
5. Run the unfiltered root gate:

   ```powershell
   dotnet test --logger "console;verbosity=minimal"
   ```

6. Update every affected `PublicAPI.Unshipped.txt`, alias pin, package boundary, serialization
   contract, and project list in the same slice.
7. Run `git diff --check`.
8. Grill the diff: unused additions, unverified claims, and unrelated changes.
9. Commit only the slice's files at the green boundary. Never stage `conversation.txt` or any
   pre-existing dirty file without explicit intent.

Mocks/fakes are allowed only for true external boundaries: `IChatClient`, MCP transport/server,
human approval input, and `TimeProvider`/schedule driver. Orleans journals, grain calls, MAF session
serialization, MAF checkpointing, mutation ledgers, and module activation use their real
implementations.

---

## Task 1: Make capability requests causal before invocation

**Why first:** every later typed request, tool call, approval, and Task fact relies on truthful
causality. The current filter records `CapabilityCall` only after success and the target never sees
it.

**Files:**

- Delete: `src/DigitalBrain.Abstractions/CapabilityCall.cs`
- Create: `src/DigitalBrain.Abstractions/CapabilityRequested.cs`
- Create: `src/DigitalBrain.Abstractions/CapabilityCompleted.cs`
- Create: `src/DigitalBrain.Abstractions/CapabilityFailed.cs`
- Create: `src/DigitalBrain.Abstractions/CapabilityRejected.cs`
- Modify: `src/DigitalBrain.Abstractions/PublicAPI.Unshipped.txt`
- Modify: `src/DigitalBrain.Kernel/OutgoingReificationFilter.cs`
- Create: `src/DigitalBrain.Kernel/IncomingReificationFilter.cs`
- Modify: `src/DigitalBrain.Kernel/DigitalBrainSiloBuilderExtensions.cs`
- Modify: `src/DigitalBrain.Kernel/Neuron.cs`
- Modify: `tests/DigitalBrain.Tests/SerializationContracts.cs`
- Modify: `tests/DigitalBrain.Simulations/EchoNeuron.cs`
- Modify: `tests/DigitalBrain.Simulations/Fabric.feature`

**Red proof:**

- Replace the old scenario with scenarios proving:
  - caller outgoing contains `CapabilityRequested` before the target method executes;
  - target incoming contains the same `SynapseId`;
  - a synapse emitted by the target has that request as causation and retains correlation;
  - caller records `CapabilityCompleted`, `CapabilityFailed`, or `CapabilityRejected`;
  - generic facts contain no argument, result, secret, or exception payload.
- Add a target that checks its incoming journal from inside the method, plus failing and
  owner-rejected targets.
- Run:

  ```powershell
  dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
  ```

  Expected red: the request is absent before invocation and failure/rejection outcomes are absent.

**Green implementation:**

- Stamp `CapabilityRequested` with contract, method, and target identity only.
- Commit it in `OutgoingReificationFilter` before `context.Invoke()`.
- Carry its `SynapseDelivery` through a reserved Kernel `RequestContext` key.
- In `IncomingReificationFilter`, validate the target and caller, commit the same delivery to the
  target incoming journal, establish it as the current causal context, then invoke.
- Record one outcome in `finally`/typed catches without exception text.
- Keep the owner authorization filter authoritative; `CapabilityRejected` reflects its refusal.
- Remove `Neuron.ReifyCapabilityCallAsync` and expose only internal Kernel operations needed by both
  filters.

**Boundary gate:** Kernel still references no AI, MAF, MCP, provider, Tasks, or Time assembly.

**Commit:** `kernel: make capability requests causally durable`

## Task 2: Introduce the independent Tasks lifecycle with a scripted worker

**Why now:** this proves Task semantics without AI and creates the `IWorker` seam that AI consumes.

**Files:**

- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/DigitalBrain.Modules.Tasks.Contracts.csproj`
- Create: `src/DigitalBrain.Abstractions/CommandId.cs`
- Modify: `src/DigitalBrain.Abstractions/PublicAPI.Unshipped.txt`
- Modify: `tests/DigitalBrain.Tests/SerializationContracts.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/ITask.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/IWorker.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/TaskCommands.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/TaskState.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/TaskSnapshot.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/Attempts.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/AttemptFacts.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/TaskBlockers.cs`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Tasks.Contracts/PublicAPI.Unshipped.txt`
- Create: `modules/DigitalBrain.Modules.Tasks/DigitalBrain.Modules.Tasks.csproj`
- Create: `modules/DigitalBrain.Modules.Tasks/TasksModule.cs`
- Create: `modules/DigitalBrain.Modules.Tasks/TaskNeuron.cs`
- Create: `modules/DigitalBrain.Modules.Tasks/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Tasks/PublicAPI.Unshipped.txt`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Create: `tests/DigitalBrain.Tests/TaskContracts.cs`
- Create: `tests/DigitalBrain.Simulations/TaskLifecycleContracts.cs`

**Red proof:**

- Compile a concrete test `Goal`, `Result`, `Failure`, and non-AI `ScriptedWorker : Neuron, IWorker`.
- Prove start idempotency by `CommandId`, exactly one active Attempt, strict revision fencing,
  caller/worker matching, Waiting blocker state, immutable terminal success, copied result/evidence,
  truthful cancellation race, and successor-only retry after terminal state.
- Prove Tasks Contracts reaches only Abstractions and contains no AI/MAF/MEAI/MCP references.

**Green implementation:**

- Persist Goal, policy, command receipts, state, revision, active Attempt, accepted result/failure,
  blocker, and evidence with Orleans durable state.
- Add and consume the shared non-empty `CommandId` value type in the first slice that needs domain
  command idempotency.
- Make `StartAsync` create Attempt revision zero and call the worker only after Task state commits.
- Consume typed Attempt facts through `IHandle<T>` methods; validate Task, Worker, Attempt, Revision,
  delivery caller, and lifecycle before changing state.
- For retryable failure, enter `Waiting(RetryScheduled)` and use a Tasks-private reserved reminder.
  Do not reference `DigitalBrain.Time`.
- Treat `AttemptOutcomeUncertain` as Waiting and never auto-retry.
- Keep `TaskNeuron` internal; expose `ITask` as the stable vocabulary.

**Commit:** `tasks: add durable task and worker contracts`

## Task 3: Replace string AI calls with the MEAI boundary and real MAF agents

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `modules/DigitalBrain.Modules.AI.Contracts/DigitalBrain.Modules.AI.Contracts.csproj`
- Modify: `modules/DigitalBrain.Modules.AI.Contracts/ILLM.cs`
- Modify: `modules/DigitalBrain.Modules.AI.Contracts/IAgent.cs`
- Modify: `modules/DigitalBrain.Modules.AI.Contracts/IGroupChat.cs`
- Modify: `modules/DigitalBrain.Modules.AI.Contracts/PublicAPI.Unshipped.txt`
- Modify: `modules/DigitalBrain.Modules.AI/DigitalBrain.Modules.AI.csproj`
- Modify: `modules/DigitalBrain.Modules.AI/LLM.cs`
- Create: `modules/DigitalBrain.Modules.AI/Agent.cs`
- Create: `modules/DigitalBrain.Modules.AI/NeuronChatClient.cs`
- Create: `modules/DigitalBrain.Modules.AI/MafAgentFactory.cs`
- Modify: `modules/DigitalBrain.Modules.AI/PublicAPI.Unshipped.txt`
- Modify: `tests/DigitalBrain.Tests/AIContracts.cs`
- Create: `tests/DigitalBrain.Tests/MafPackageContracts.cs`

**Red proof:**

- Assert `ILLM` and `IAgent` expose only
  `RespondAsync(IReadOnlyList<ChatMessage>) -> Task<ChatResponse>`.
- Assert `IGroupChat : IAgent, IWorker`.
- Compile the exact MAF APIs used by the implementation:
  `ChatClientAgent`, `AIAgent.CreateSessionAsync`, session serialize/deserialize, workflow builders,
  `AIContextProvider`, tool approval, Lockstep execution, checkpoint manager, and checkpoint store.
- With a deterministic `IChatClient`, prove:
  - `Llama32 : LLM, ILlama32` uses only `[Llm<Llama32>] IChatClient`;
  - a concrete `Agent` uses its typed LLM through `NeuronChatClient`;
  - instructions are agent-owned and caller `ChatOptions` cannot be supplied;
  - two calls to an ordinary Agent do not share a session.

**Green implementation:**

- Add centrally pinned MAF 1.13.0 packages. Defer the MCP package entry until the Google slice first
  consumes it.
- Change `LLM` to call `IChatClient.GetResponseAsync(messages)`.
- Adapt typed `ILLM` neuron proxies to private `IChatClient` instances for MAF.
- Reconstruct `ChatClientAgent` per Agent call from the typed model, dynamic instructions, and
  declared capability roots.
- Do not create a DigitalBrain transcript or agent loop.

**Commit:** `ai: adopt MEAI messages and MAF agent semantics`

## Task 4: Ask multiple models and reconcile with one durable MAF session

**Files:**

- Create: `modules/DigitalBrain.Modules.AI/Participant.cs`
- Create: `modules/DigitalBrain.Modules.AI/Concurrent.cs`
- Create: `modules/DigitalBrain.Modules.AI/GroupChat.cs`
- Create: `modules/DigitalBrain.Modules.AI/MafParticipantAdapter.cs`
- Create: `modules/DigitalBrain.Modules.AI/OrchestrationState.cs`
- Create: `modules/DigitalBrain.Modules.AI/SessionCompatibility.cs`
- Modify: `modules/DigitalBrain.Modules.AI/PublicAPI.Unshipped.txt`
- Create: `tests/DigitalBrain.Simulations/AIOrchestrationContracts.cs`

**Red proof:**

- Define a test `Concurrent` with `Participant<ILlama32>` and `Participant<IGpt56>`.
- Prove both receive the same immutable messages and neither sees the other's answer.
- Define a test `GroupChat` that consumes their answers and returns a reconciled response.
- Deactivate/reactivate the group neuron and prove the next turn restores exactly one serialized MAF
  `AgentSession`; assert there is no second transcript field.
- Change a participant/model/instruction fingerprint and prove old state is rejected with an
  explicit reset/migration-required result rather than silently deserialized.

**Green implementation:**

- Resolve each participant from its typed contract and `NeuronId`; constructor injection is not
  participant declaration.
- Wrap raw `ILLM` participants in private stateless MAF agents.
- Use `AgentWorkflowBuilder.BuildConcurrent` and the MAF group-chat builder; do not implement custom
  turn selection or message routing.
- Persist encrypted-at-rest serialized `AgentSession`, format version, and composition fingerprint in
  the orchestration neuron. Do not persist another transcript.

**Commit:** `ai: add durable concurrent and group orchestration`

## Task 5: Bridge GroupChat to Tasks through one fenced Lockstep superstep

**Files:**

- Create: `modules/DigitalBrain.Modules.AI/ExecutionLease.cs`
- Create: `modules/DigitalBrain.Modules.AI/AIWorkerState.cs`
- Create: `modules/DigitalBrain.Modules.AI/FencedWorkflowRunner.cs`
- Create: `modules/DigitalBrain.Modules.AI/OrleansCheckpointStore.cs`
- Modify: `modules/DigitalBrain.Modules.AI/GroupChat.cs`
- Create: `tests/DigitalBrain.Simulations/AIWorkerContracts.cs`
- Modify: `hosts/DigitalBrain.ProbeHost/Neurons.cs`
- Modify: `hosts/DigitalBrain.ProbeHost/Program.cs`
- Modify: `tests/DigitalBrain.HostTests/HostedRestart.cs`

**Red proof:**

- Prove `AcceptAsync`/`ContinueAsync`/`CancelAsync` validate and persist, schedule work, and return
  without running a model inline.
- Prove one active lease advances exactly one MAF Lockstep superstep and persists the MAF checkpoint
  before emitting `AttemptAdvanced`.
- Restart the silo after the first superstep; prove the same session/checkpoint resumes without
  repeating the already-completed executor.
- Prove duplicate, stale, future, cancelled, and late lease results cannot overwrite current worker
  state.
- Prove pending MAF approval/input maps to typed Task Waiting facts; MAF `RunStatus` never appears in
  Tasks contracts.

**Green implementation:**

- Store Attempt, revision, workflow fingerprint, checkpoint reference, lease generation, and lease
  deadline in the GroupChat neuron.
- Run the leased superstep in a private, owner-bound Orleans runner grain. It is infrastructure: no
  `INeuron`, journal, registry entry, or scripting contract.
- Implement the real MAF JSON checkpoint manager over Orleans durable storage.
- Commit worker state/checkpoint before asking the Task for the next continuation.
- On runner loss, expire and redispatch the same fenced lease; accept only the active generation.

**Commit:** `ai: execute task attempts one durable lockstep at a time`

## Task 6: Adapt private capability catalogs into MAF context and approval

**Files:**

- Create: `src/DigitalBrain.Capabilities/DigitalBrain.Capabilities.csproj`
- Create: `src/DigitalBrain.Capabilities/CapabilityOperation.cs`
- Create: `src/DigitalBrain.Capabilities/CapabilityTool.cs`
- Create: `src/DigitalBrain.Capabilities/CapabilityToolCatalog.cs`
- Create: `src/DigitalBrain.Capabilities/CapabilityToolInvocation.cs`
- Create: `src/DigitalBrain.Capabilities/CapabilityApproval.cs`
- Create: `src/DigitalBrain.Capabilities/CapabilityToolResult.cs`
- Create: `src/DigitalBrain.Capabilities/ICapabilityToolSource.cs`
- Create: `src/DigitalBrain.Capabilities/PublicAPI.Shipped.txt`
- Create: `src/DigitalBrain.Capabilities/PublicAPI.Unshipped.txt`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Modify: `modules/DigitalBrain.Modules.AI/DigitalBrain.Modules.AI.csproj`
- Create: `modules/DigitalBrain.Modules.AI/CapabilityAIContextProvider.cs`
- Create: `modules/DigitalBrain.Modules.AI/CapabilityToolSelector.cs`
- Create: `modules/DigitalBrain.Modules.AI/CapabilityToolApproval.cs`
- Create: `modules/DigitalBrain.Modules.AI/CapabilityInvocationLease.cs`
- Modify: `src/DigitalBrain.Kernel/CapabilityRequestContext.cs`
- Modify: `src/DigitalBrain.Kernel/IncomingReificationFilter.cs`
- Create: `tests/DigitalBrain.Tests/CapabilityBoundaryContracts.cs`
- Create: `tests/DigitalBrain.Simulations/CapabilityToolContracts.cs`

**Red proof:**

- Register a deterministic fake `ICapabilityToolSource` with more schemas than the configured token
  budget.
- Prove exact relevant tools are injected through MAF `AIContextProvider`, prior tools remain sticky,
  and no fixed count truncates the catalog.
- Prove `FindCapabilityTools` can add only an unseen tool from the pinned/granted revision and makes
  finite progress.
- Prove read tools can auto-approve, mutation/unknown tools pause for exact human approval, and an
  approver agent cannot authorize.
- Prove tool name, exact canonical arguments, catalog revision, and fingerprint are bound to the
  approval.
- Prove the model-visible tool calls the fake source through the semantic capability neuron path and
  produces the Kernel causal request/outcome facts.
- Prove a private non-neuron runner can carry only a current, owner-bound, one-use invocation lease;
  the target journals the precommitted request, while replay, stale revision, wrong owner, and an
  unleased raw `RequestContext` value are rejected before the module-private method runs.
- Prove client/contracts/behavior-visible packages cannot reference `DigitalBrain.Capabilities`.

**Green implementation:**

- Use schema token cost as the inclusion budget. If all exact schemas fit, include all. Otherwise use
  deterministic lexical retrieval first; add an embedding index only after a measured miss proves it
  necessary.
- Map selected exact schemas to `AIFunction`; never expose a generic invoke function.
- Configure MAF tool-approval middleware as the pause/resume coordinator.
- Have the integration neuron independently validate the same approval fingerprint.
- Before an off-turn runner invokes a tool, call back to the owning orchestration neuron to commit
  `CapabilityRequested` and mint a one-use lease for the active Attempt/revision. Reject replay,
  staleness, and cross-owner use.
- Extend the Kernel causal bridge only through that failing leased-runner proof. Keep the raw
  `RequestContext` key private; if the proof requires a new public runtime contract, stop and record
  that architecture decision before adding it.

**Commit:** `ai: project semantic capabilities into approved MAF tools`

## Task 7: Add Google Gmail as a read-only semantic capability root

**Files:**

- Create: `modules/DigitalBrain.Modules.Google.Contracts/DigitalBrain.Modules.Google.Contracts.csproj`
- Create: `modules/DigitalBrain.Modules.Google.Contracts/IGmail.cs`
- Create: `modules/DigitalBrain.Modules.Google.Contracts/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Google.Contracts/PublicAPI.Unshipped.txt`
- Create: `modules/DigitalBrain.Modules.Google/DigitalBrain.Modules.Google.csproj`
- Create: `modules/DigitalBrain.Modules.Google/GoogleModule.cs`
- Create: `modules/DigitalBrain.Modules.Google/Gmail.cs`
- Create: `modules/DigitalBrain.Modules.Google/GmailCapabilityToolSource.cs`
- Create: `modules/DigitalBrain.Modules.Google/GmailMcpClient.cs`
- Create: `modules/DigitalBrain.Modules.Google/GmailToolPolicy.cs`
- Create: `modules/DigitalBrain.Modules.Google/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Google/PublicAPI.Unshipped.txt`
- Create: `modules/DigitalBrain.Modules.Google.Aspire.Hosting/DigitalBrain.Modules.Google.Aspire.Hosting.csproj`
- Create: `modules/DigitalBrain.Modules.Google.Aspire.Hosting/GoogleHostingExtensions.cs`
- Create: `modules/DigitalBrain.Modules.Google.Aspire.Hosting/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Google.Aspire.Hosting/PublicAPI.Unshipped.txt`
- Modify: `Directory.Packages.props`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Create: `tests/DigitalBrain.Tests/GoogleContracts.cs`
- Create: `tests/DigitalBrain.Simulations/GmailCapabilityContracts.cs`

**Red proof:**

- Assert the only public Gmail contract is semantic `IGmail : INeuron`; no MCP DTO, tool name,
  dictionary, JSON invocation method, or hand-written CRUD surface is public.
- Against a fake MCP server at the transport boundary, list a pinned Gmail catalog and execute one
  read (`search_threads`, followed by `get_thread` only if required by the story).
- Prove catalog/schema changes are rejected until the configured pin is updated.
- Prove Gmail classifies only admitted read tools as `Read`; every unknown tool is `Unknown`.
- Prove `brain.AddModule<GoogleModule>(google => google.WithGmail())` creates documented OAuth
  parameters/resource references once, marks secrets correctly, and never projects literal secrets.

**Green implementation:**

- Use the official Gmail MCP endpoint and `McpClient`; keep all MCP types in the Google runtime.
- Map `McpClientTool` metadata to exact neutral capability schemas.
- Route invocation through the module-private method on the `Gmail` neuron, then through the MCP
  client.
- At the beginning of this slice, verify the current official Developer Preview authentication
  contract from the primary source and encode only those required Aspire parameters. If it differs
  from the plan, record the factual adjustment in the architecture decision log before code.
- Keep OAuth/token refresh and secret descriptions in Google Aspire hosting.

**Commit:** `google: add official Gmail MCP capability`

## Task 8: Add Salesforce with durable approved mutation and reconciliation

**Files:**

- Create: `modules/DigitalBrain.Modules.Salesforce.Contracts/DigitalBrain.Modules.Salesforce.Contracts.csproj`
- Create: `modules/DigitalBrain.Modules.Salesforce.Contracts/ISalesforce.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce.Contracts/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Salesforce.Contracts/PublicAPI.Unshipped.txt`
- Create: `modules/DigitalBrain.Modules.Salesforce/DigitalBrain.Modules.Salesforce.csproj`
- Create: `modules/DigitalBrain.Modules.Salesforce/SalesforceModule.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce/SalesforceCapabilityToolSource.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce/SalesforceMcpClient.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce/SalesforceToolPolicy.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce/MutationCommand.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce/MutationLedger.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Salesforce/PublicAPI.Unshipped.txt`
- Create: `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/DigitalBrain.Modules.Salesforce.Aspire.Hosting.csproj`
- Create: `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/SalesforceHostingExtensions.cs`
- Create: `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/PublicAPI.Unshipped.txt`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Create: `tests/DigitalBrain.Tests/SalesforceContracts.cs`
- Create: `tests/DigitalBrain.Simulations/SalesforceMutationContracts.cs`

**Red proof:**

- Assert the only public capability root is `ISalesforce : INeuron`.
- Against a fake hosted MCP server, admit the exact `platform/sobject-mutations` schema needed to
  update one Account description.
- Prove state transitions
  `Proposed -> AwaitingApproval -> Approved -> Invoking -> Completed`.
- Prove same `CommandId` + fingerprint returns the recorded outcome; same ID + different fingerprint
  rejects.
- Prove approval is bound to exact arguments and a typed caller cannot bypass it.
- Crash after committing `Invoking` but before receiving the MCP response. On recovery, prove a read
  reconciliation decides `Completed` or `OutcomeUncertain`; it never blindly repeats the mutation.
- Prove an uncertain outcome makes the Task wait and is never auto-retried.
- Prove Aspire hosting creates documented Salesforce OAuth/instance parameters once and does not leak
  secrets.

**Green implementation:**

- Keep the application-specific `SetAccountDescription` Goal/Result in the PoC application, not the
  framework contract.
- Canonicalize exact MCP arguments and hash them for the fingerprint.
- Persist the integration-owned ledger in the Salesforce neuron and commit `Invoking` before MCP.
- Pass `CommandId` as a provider idempotency key only when the official tool supports it; never claim
  exactly-once.
- Reconcile by reading the target field through an admitted read tool. If the intended value cannot
  be proven, record `OutcomeUncertain`.
- At slice start, verify the current official Hosted MCP authentication contract and mutation schema
  from primary sources; record factual drift before adapting.

**Commit:** `salesforce: add approved reconciled MCP mutation`

## Task 9: Add deterministic one-shot Countdown and Reminder neurons

**Files:**

- Create: `modules/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/ICountdown.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/IReminder.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/CountdownCommands.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/ReminderCommands.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/ScheduleSnapshots.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/ScheduleFacts.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/PublicAPI.Unshipped.txt`
- Create: `modules/DigitalBrain.Modules.Time/DigitalBrain.Modules.Time.csproj`
- Create: `modules/DigitalBrain.Modules.Time/TimeModule.cs`
- Create: `modules/DigitalBrain.Modules.Time/Countdown.cs`
- Create: `modules/DigitalBrain.Modules.Time/Reminder.cs`
- Create: `modules/DigitalBrain.Modules.Time/IScheduleDriver.cs`
- Create: `modules/DigitalBrain.Modules.Time/OrleansScheduleDriver.cs`
- Create: `modules/DigitalBrain.Modules.Time/PublicAPI.Shipped.txt`
- Create: `modules/DigitalBrain.Modules.Time/PublicAPI.Unshipped.txt`
- Modify: `src/DigitalBrain.Kernel/Neuron.cs`
- Modify: `src/DigitalBrain.Testing/Simulation.cs`
- Modify: `src/DigitalBrain.Testing/PublicAPI.Unshipped.txt`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Create: `tests/DigitalBrain.Tests/TimeContracts.cs`
- Create: `tests/DigitalBrain.Simulations/TimeLifecycleContracts.cs`

**Red proof:**

- With a controlled `TimeProvider`, prove Start/Schedule only from Unscheduled, expected-revision
  Reschedule/Cancel, idempotent commands, and new-generation Restart after terminal state.
- Prove due facts are never early, are sent once to the explicit same-owner destination, and carry no
  arbitrary payload.
- Race activation-local timer and durable reminder callbacks and prove one elapsed/due fact.
- Simulate a late callback from the prior revision and prove it is ignored.
- Deactivate before due time, advance simulated time, reactivate, and prove one `Recovered` fact.
- Prove Kernel private reminder names remain reserved and unknown reminder names are not treated as
  the outbox reminder.

**Green implementation:**

- Use `TimeProvider` for all schedule math.
- Persist schedule definition, lifecycle, generation, revision, command receipts, destination, and
  occurrence receipt in the Time neuron.
- Keep `IScheduleDriver` internal. Production registers an activation-local timer plus a durable
  reminder backstop; tests register a deterministic driver.
- Split Kernel's sealed `ReceiveReminder` path into reserved private reminder dispatch so Time does
  not inherit “every reminder means outbox.”
- Add `Simulation.AdvanceTimeByAsync(...)` without wall-clock sleeps.

**Commit:** `time: add durable one-shot countdown and reminder`

## Task 10: Make the first production durability profile complete

**Files:**

- Modify: `src/DigitalBrain.Aspire.Hosting/BrainHosting.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/PublicAPI.Unshipped.txt`
- Modify: `src/DigitalBrain.Kernel/JournalStorageHosting.cs`
- Create: `src/DigitalBrain.Kernel/ReminderStorageHosting.cs`
- Modify: `hosts/DigitalBrain.TestingAppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.TestingAppHost/DigitalBrain.TestingAppHost.csproj`
- Modify: `hosts/DigitalBrain.Host/Program.cs`
- Modify: `hosts/DigitalBrain.Host/DigitalBrain.Host.csproj`
- Modify: `tests/DigitalBrain.Tests/ModuleActivationContracts.cs`
- Create: `tests/DigitalBrain.Tests/DurabilityProfileContracts.cs`
- Modify: `tests/DigitalBrain.HostTests/HostedRestart.cs`

**Red proof:**

- Assert `.WithAzureStorage()` creates one Azure Storage resource and projects Blob journal, Table
  clustering, and Table reminder references.
- Assert `.WithDevelopmentStores()` is explicitly marked non-durable.
- Refuse production startup when in-memory clustering/reminders are selected.
- Start the hosted system on Azurite, create an active Task/GroupChat checkpoint and future Reminder,
  restart the silo resource, and prove both resume.

**Green implementation:**

- Implement one complete profile only: Blob-backed journals plus Table-backed clustering/reminders
  from one Aspire Azure Storage resource.
- Keep local development on Azurite and deployment on the provisioned resource.
- Let `silo.AddDigitalBrain()` consume projected configuration and register all selected module
  runtimes; no module-specific call appears in the silo.

**Commit:** `hosting: add complete azure durability profile`

## Task 11: Prove the complete compiled Foundation PoC

**Files:**

- Create: `samples/DigitalBrain.Foundation/DigitalBrain.Foundation.csproj`
- Create: `samples/DigitalBrain.Foundation/FoundationGoal.cs`
- Create: `samples/DigitalBrain.Foundation/FoundationResult.cs`
- Create: `samples/DigitalBrain.Foundation/FoundationFailure.cs`
- Create: `samples/DigitalBrain.Foundation/FoundationAgents.cs`
- Create: `samples/DigitalBrain.Foundation/FoundationGroup.cs`
- Create: `samples/DigitalBrain.Foundation/FoundationWorker.cs`
- Create: `hosts/DigitalBrain.FoundationHost/DigitalBrain.FoundationHost.csproj`
- Create: `hosts/DigitalBrain.FoundationHost/Program.cs`
- Create: `hosts/DigitalBrain.FoundationAppHost/DigitalBrain.FoundationAppHost.csproj`
- Create: `hosts/DigitalBrain.FoundationAppHost/AppHost.cs`
- Create: `tests/DigitalBrain.FoundationTests/DigitalBrain.FoundationTests.csproj`
- Create: `tests/DigitalBrain.FoundationTests/FoundationStory.cs`
- Create: `tests/DigitalBrain.FoundationTests/FakeMcpServers.cs`
- Create: `tests/DigitalBrain.FoundationTests/DeterministicChatClients.cs`
- Modify: `DigitalBrain.slnx`

**Red proof:**

- Write one test named:
  `A_durable_task_reads_Gmail_compares_two_models_gets_approval_updates_Salesforce_and_schedules_follow_up`.
- Use real Orleans, journals, module activation, Tasks, MAF workflows/session/checkpoints,
  capability selection/approval, integration mutation ledger, and Time neurons.
- Fake only `IChatClient`, the two MCP servers at transport, human approval input, and clock/driver.
- Stop the silo after the first Lockstep superstep and before approval; restart the same resource.
- Assert:
  - Gmail read occurred once;
  - both typed models answered independently;
  - group reconciliation resumed without replaying the completed superstep;
  - Salesforce mutation was not sent before exact human approval;
  - the mutation occurred once or reconciled to proven completion;
  - Countdown and one-shot Reminder each delivered once;
  - Task terminal Result is typed and references evidence facts;
  - caller and target journals show one causal chain from Task request through integration outcome.

**Green implementation:**

- Keep `FoundationGoal`, `FoundationResult`, `FoundationFailure`, and Account-description vocabulary in
  the sample application.
- Configure AppHost explicitly:

  ```csharp
  var brain = builder
      .AddBrain("brain")
      .WithAzureStorage();

  brain.AddModule<TasksModule>();
  brain.AddModule<AIModule>(ai => ai
      .WithLlm<Llama32>()
      .WithLlm<Gpt56>());
  brain.AddModule<GoogleModule>(google => google.WithGmail());
  brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
  brain.AddModule<TimeModule>();
  ```

- Keep the silo configuration to `silo.AddDigitalBrain()`.
- Do not add a framework shortcut until a second application demonstrates repetition.

**Commit:** `poc: prove the durable five-module foundation story`

## Task 12: Close the implementation proof and remove planning residue

**Files:**

- Modify: `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`
- Modify: `APPROVED-ARCHITECTURE-DECISIONS.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `CONTEXT.md`
- Delete or archive only obsolete progress/checklist documents identified by exact path after a
  repository search; retain durable decision records and this implementation rationale.

**Proof:**

- Search forbidden architecture:

  ```powershell
  rg -n "AskModelAsync|Task<string> AskAsync|ModelTier|ModelDescriptor|ILlmDefinition|AddAIModule|AddDigitalBrainModels|WithModel\\(|generic raw invoke" src modules hosts samples tests
  ```

- Verify every packable public type has an API baseline and every alias is pinned.
- Verify the dependency graph and module-selection tests.
- Run the website gate if rendered architecture/specification content changed:

  ```powershell
  Set-Location website
  node tools/render-specification.mjs
  node --test tests/*.test.mjs
  Set-Location ..
  ```

- Run:

  ```powershell
  dotnet test --logger "console;verbosity=minimal"
  git diff --check
  git status --short
  ```

- Record exact pass/fail/skip counts. Zero failures and zero skips are required for completion.
- Compare final HEAD and status with the task-start snapshot. Surface every unrelated change.

**Commit:** `docs: record the proven foundation architecture`

---

## Requirement-to-proof map

| Ratified requirement | First red proof | Final proof |
|---|---|---|
| Kernel contains only neuron/synapse mechanics | Task 1 assembly boundary | Task 12 forbidden search |
| Request is visible before invocation with causal target journal | Task 1 Fabric scenarios | Task 11 causal chain |
| Tasks is independent of AI | Task 2 package boundary | Task 11 durable Task |
| Public AI wire is MEAI, not strings or caller options | Task 3 contract test | Task 11 typed clients |
| MAF owns orchestration and session | Tasks 3–4 MAF API/session tests | Task 11 group reconciliation |
| One Lockstep superstep per fenced lease | Task 5 worker/restart test | Task 11 mid-attempt restart |
| Same question can reach several typed models | Task 4 Concurrent test | Task 11 two-model comparison |
| Integration roots are semantic, MCP remains private | Tasks 6–8 boundaries | Task 11 official-shaped fake servers |
| Progressive exact tools, no fixed count/raw invoke | Task 6 catalog tests | Task 11 selected Gmail/Salesforce tools |
| Human authority and exact mutation fingerprint | Tasks 6 and 8 approval tests | Task 11 pre/post approval assertions |
| No blind retry after uncertain external effect | Task 8 crash/reconcile test | Task 11 ledger evidence |
| Countdown/Reminder are durable semantic neurons | Task 9 lifecycle/recovery tests | Task 11 follow-up delivery |
| AppHost explicit, silo encapsulated | Task 10 hosting contracts | Task 11 host composition |
| Five-module PoC and nothing broader | Scope-lock checks in every task | Task 12 forbidden/dependency gates |

## Stop conditions

Stop the current slice and update the decision record before continuing if:

- a required MAF or MCP API differs from the compiler-verified package surface;
- an official MCP server no longer provides the consumed capability or authentication flow;
- the off-turn runner cannot preserve owner authorization and causal request identity;
- MAF Lockstep cannot resume without repeating a completed external effect;
- Salesforce reconciliation cannot distinguish completion from uncertainty;
- a public contract needs raw JSON, MCP, provider, MAF workflow, or Orleans timer/reminder types;
- a new framework abstraction has only one adapter or no consumer in the Foundation story.

These are evidence-triggered corrections. They do not reopen Memory, Flutter, tiers, broad MCP,
recurrence, or runtime behaviors.
