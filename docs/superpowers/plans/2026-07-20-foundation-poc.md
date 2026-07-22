# Foundation PoC Implementation Plan

> **For agentic workers:** Execute this plan with the repository's `tdd` skill. Use
> `subagent-driven-development` only when the user explicitly requests delegated execution;
> otherwise execute inline, one green slice at a time.

**Goal:** Prove that DigitalBrain can durably execute one useful cross-module task through Tasks,
AI, Google, Salesforce, and Time without leaking provider, MCP, or orchestration mechanics into the
Kernel or public behavior vocabulary.

Tasks 1 through 8 are complete; git history holds their detail from `3abdc219` through `2f21c9b4`.
That span starts at the causal capability-request slice and ends at the restart stabilization, taking
in the Tasks contracts at `51e35c73`, the MEAI and MAF adoption at `ef362563`, the Lockstep worker
bridge beginning at `6fcbb734`, and the Gmail and Salesforce slices at `fe058caa` and `05eb40a2`.

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
absent from contracts packages and generated semantic discovery. The future contract-only behavior
compiler must exclude it, but that compiler is not current evidence. AI converts a selected
`CapabilityTool` to an exact MAF/MEAI `AIFunction`; the model never receives
`ICapabilityToolSource.InvokeAsync`, an MCP client, or a generic raw-invoke tool.

Every source implementation routes invocation through the module-private method on the semantic
capability neuron. For normal neuron calls, Kernel filters commit and propagate
`CapabilityRequested`. For a private off-turn MAF runner, the owning orchestration neuron first
commits an exact request and Kernel issues the single opaque-public, non-semantic
`DigitalBrain.Kernel.CapabilityDelegation` for the physical runner source. Kernel's private context
carrier delivers it; consumers never write a raw `RequestContext` value. The integration neuron
journals the same incoming request before its module-private method runs. Every off-turn participant
or integration call receives a distinct precommitted request and delegation. This preserves the
approved causal rail without pretending the runner is a public neuron or putting Task revision,
run, checkpoint, MAF, approval, integration, lease, generation, or renewal semantics in Kernel.

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

- Modify: `docs/architecture.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `docs/concepts.md`

**Proof:**

- Search forbidden architecture:

  ```powershell
  rg -n "AskModelAsync|Task<string> AskAsync|ModelTier|ModelDescriptor|ILlmDefinition|AddAIModule|AddDigitalBrainModels|WithModel\\(|generic raw invoke" src modules hosts samples tests
  ```

- Verify every packable public type has an API baseline and every alias is pinned.
- Verify the dependency graph and module-selection tests.
- Run the website gate if rendered architecture/specification content changed:

  ```powershell
  Set-Location docs
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
| One Lockstep superstep per recoverable run | Task 5 worker/restart test | Task 11 mid-attempt restart |
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
