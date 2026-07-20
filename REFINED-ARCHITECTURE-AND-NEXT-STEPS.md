# Refined Architecture and Next Steps

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore DigitalBrain to a small neuron kernel with independently shipped, convention-driven modules, beginning with a typed AI module and module-owned Aspire hosting.

**Architecture:** Kernel routes incoming and outgoing synapses and knows nothing about AI, Flutter, Google, Salesforce, or Memory. Public namespaces and type names are the framework vocabulary. AppHost explicitly selects modules and their infrastructure; `silo.AddDigitalBrain()` uses a generated catalog to activate the selected runtime modules.

**Tech Stack:** .NET 10, Orleans 10, Aspire 13.4, Microsoft Agent Framework,
`Microsoft.Extensions.AI`, OpenAI, OllamaSharp, xUnit v3, Roslyn incremental generation.

## Global Constraints

- This is a breaking hard cut. Do not add compatibility shims or obsolete forwarding types.
- Keep the kernel independent of provider SDKs and module-specific configuration.
- A module is selected exactly once in AppHost.
- The silo contains only `silo.AddDigitalBrain()` for DigitalBrain composition.
- `IChatClient` is private to concrete AI model neurons.
- Namespaces and type names are canonical architecture and future natural-language vocabulary.
- Runtime assembly scanning is forbidden. Generate catalogs during compilation.
- The AppHost module manifest is the sole activation authority.
- Vector search is a derived index over the typed catalog, never the source of truth.
- Microsoft Agent Framework owns AI orchestration; DigitalBrain supplies durable typed boundaries.
- A Task is durable desired-outcome identity; a MAF Workflow is an Attempt implementation.
- Public Time neurons are semantic schedules; raw Orleans timers and reminders remain infrastructure.
- Memory is outside this implementation.
- Preserve a green root gate at every commit boundary.

---

## 1. Honest status on 2026-07-20

The hard cut started at commit `f5ae864651c8d37edbbf2827d893d8e6eac05219`. The durable slices
through authoritative AppHost activation and duplicate-client removal are committed on `master`.

Fresh completion evidence:

```text
DigitalBrain.Tests:       125 passed
DigitalBrain.Simulations:  50 passed
DigitalBrain.HostTests:     5 passed
Website contracts:         16 passed
NuGet packages/symbols:    11 / 11 packed
Package-only samples:       2 restored, built, and ran from an empty cache
Dependency hygiene:         no vulnerable, deprecated, or floating dependencies
```

Passing foundation tests do not mean the product vision is complete.

| Area | Status | Evidence |
|---|---:|---|
| Durable neuron and synapse kernel | About 80% | Journals, bounded dedupe, owner checks, broadcast delivery, observation, and host restart tests exist |
| Approved framework architecture | About 65–70% | Kernel/module boundaries, typed AI, and hosting composition now match the ratified design |
| Typed AI module | About 60% | `ILLM`, `Llama32`, and `Gpt56` exist; agent and group-chat implementations do not |
| MAF-aligned agents and orchestration | Ratified, 0% implemented | Ownership, session, checkpoint, compaction, and adapter boundaries are settled below |
| Durable Tasks module | Ratified, 0% implemented | `ITask`/`IWorker`, Attempt fencing, lifecycle, cancellation, and AI dependency direction are settled below |
| Semantic Time module | Partially ratified, 0% implemented | Countdown/reminder semantics, durability, recurrence classes, testing, and hosting are settled; exact record shapes and recurrence engine remain open |
| Module-owned Aspire integration | Built for AI | AI owns Ollama/OpenAI resources, models, parameters, and silo projection |
| Generated module activation | Built | AppHost selection is validated against the generated silo catalog |
| Owner-bound client entry | Built | `DigitalBrainClient` sends and emits through the session; it exposes no raw neuron proxies |
| Natural-language-to-typed catalog | 0% | No canonical generated neuron catalog or semantic index exists |
| Script → proposal → approval → install → rollback | 0% end to end | Client surface is provisional; installation and governance rail do not exist |

The repository is not honestly in Phase 4. It is in a Phase 2b repair followed by a Phase 3 redesign.

## 2. Ratified architecture

### 2.1 Kernel

`DigitalBrain.Kernel.Neuron` owns only neuron mechanics:

- Receive and dispatch incoming synapses.
- Emit, send, and reply with outgoing synapses.
- Journal and observe traffic.
- Persist operational neuron state.
- Enforce owner, delivery, and concurrency invariants.

Kernel must not contain:

- `AskModelAsync`
- Model tiers or provider names
- `IChatClient`
- AI prompts or responses
- OAuth provider details
- Flutter contracts
- Semantic memory

### 2.2 Modules

Each domain is an independent package family:

```text
DigitalBrain.Modules.<Name>.Contracts
DigitalBrain.Modules.<Name>
DigitalBrain.Modules.<Name>.Aspire.Hosting   optional
```

Physical package names may contain `Modules` and `Contracts`. Public vocabulary does not:

```csharp
DigitalBrain.AI.ILLM
DigitalBrain.AI.Ollama.ILlama32
DigitalBrain.AI.OpenAI.IGpt56
DigitalBrain.Google.ICalendar
DigitalBrain.Google.IGmail
DigitalBrain.Salesforce.ISalesforce
DigitalBrain.Flutter.IFlutter
```

`.Contracts` references only `DigitalBrain.Abstractions`. Runtime packages own neurons and vendor adapters. Aspire hosting packages own resources, parameters, authentication setup, and projection into the silo.

### 2.3 AppHost and silo

AppHost is explicit:

```csharp
var brain = builder.AddBrain("brain");

brain.AddModule<AIModule>(ai => ai
    .WithLlm<Llama32>()
    .WithLlm<Gpt56>());
```

Each module is added once. Repeated `AddModule<AIModule>` calls are composition errors.

The silo is intentionally boring:

```csharp
builder.UseOrleans(silo => silo
    .AddDigitalBrain());
```

Compilation generates the module catalog and the complete runtime composition from referenced module
types. AppHost projects the selected module manifest and resource configuration. Core durability,
selected module activation, and module runtime wiring happen behind `AddDigitalBrain()`. Startup
fails when AppHost selects a module absent from the silo catalog.

Package reference means available. `AddModule<T>()` means selected and configured.

### 2.4 AI vocabulary and implementation

The contracts package owns DigitalBrain-native conversation contracts. `ILLM` means model inference;
`IAgent` means an agent with instructions and capabilities. They remain separate contracts even when
their transport shapes are similar:

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

The conversation boundary uses Microsoft Extensions AI `ChatMessage` and `ChatResponse`, not
framework-owned string request/response DTOs. Callers do not supply `ChatOptions`; the concrete typed
model or agent owns its model, instructions, tools, and inference configuration.

Concrete model contracts live in provider namespaces:

```csharp
namespace DigitalBrain.AI.Ollama;

public interface ILlama32 : ILLM;
```

```csharp
namespace DigitalBrain.AI.OpenAI;

public interface IGpt56 : ILLM;
```

The runtime adapts `IChatClient` exactly once:

```csharp
namespace DigitalBrain.AI;

public abstract class LLM(IChatClient chatClient) : Neuron, ILLM;
```

Concrete neurons carry identity, not configuration objects:

```csharp
namespace DigitalBrain.AI.Ollama;

public sealed class Llama32(
    [Llm<Llama32>] IChatClient chatClient)
    : LLM(chatClient), ILlama32;
```

```csharp
namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt56(
    [Llm<Gpt56>] IChatClient chatClient)
    : LLM(chatClient), IGpt56;
```

There is no `ILlmDefinition`, `ModelDescriptor`, provider enum, tier, capability score, balancing router, or fallback router. Provider and model identity come from the canonical namespace and type name. The generator/analyzer rejects names outside the supported grammar.

Only concrete `LLM` neurons may receive `[Llm<TModel>] IChatClient`. Agents consume `ILlama32`,
`IGpt56`, or another concrete model contract. `ILLM` never inherits `IAgent`, and orchestration
adapters must not pretend that a raw model is a durable agent.

### 2.5 AI Aspire hosting

AI-specific configuration lives in `DigitalBrain.Modules.AI.Aspire.Hosting`:

```csharp
brain.AddModule<AIModule>(ai => ai
    .WithLlm<Llama32>()
    .WithLlm<Gpt56>());
```

`WithLlm<Gpt56>()`:

- Creates one shared `openai-api-key` secret parameter per `AIModule`.
- Adds the Markdown description:
  `Get your key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys)`.
- Adds one OpenAI provider resource and one model resource.
- Reuses the provider and parameter for additional OpenAI models.
- Projects resource references, never secret literals.

`WithLlm<Llama32>()`:

- Creates one shared Ollama resource.
- Adds the `llama3.2` model to that resource.
- Creates no API-key parameter.
- Projects the Ollama endpoint and model identity to the silo.

The first implementation supports one provider connection per provider. Named accounts, failover, cost balancing, and per-model credentials are deliberately excluded.

### 2.6 Agents and group chat

`IAgent` is an LLM-powered conversational neuron, not a universal base interface.
Microsoft Agent Framework is the AI orchestration engine. DigitalBrain must not build a second agent
loop, group-chat engine, handoff engine, workflow engine, session format, or tool middleware stack.

Application agents compose typed capabilities:

```csharp
public sealed class MailAssistant(
    ILlama32 llama,
    IGmail gmail)
    : Agent, IMailAssistant;
```

An individual typed `IAgent` is stateless between calls unless its declared contract explicitly says
otherwise. It is a durable neuron identity and synapse boundary around a reconstructed MAF
`AIAgent`; MAF owns agent semantics and the concrete `ILLM` owns inference.

Only session-owning orchestration neurons are conversationally stateful. Each `IGroupChat` owns
exactly one serialized MAF `AgentSession` as its sole conversation state. DigitalBrain must not keep
a second transcript beside that session.

The public orchestration vocabulary is selected through typed base classes:

```text
GroupChat
Sequential
Concurrent
Handoff
Magentic
```

Orchestrations accept either typed `IAgent` neurons or raw typed `ILLM` neurons. Internal adapters
turn both into MAF `AIAgent` participants without changing their public contracts. This supports:

- Asking the same question of several models with `Concurrent`.
- Comparing independent answers.
- Running a moderated group chat.
- Sequential review or refinement.
- Explicit handoff and Magentic orchestration.

Participants are declared by typed neuron identity, for example `Participant<T>(NeuronId)`. Constructor
injection is for actual dependencies, not fake participant declarations.

MAF executors are private, reconstructed runtime objects. They have stable internal identifiers only
for checkpoint compatibility and never receive a DigitalBrain registry identity, public contract,
journal, or natural-language discovery entry. Anything requiring durable identity, authorization,
reuse, journaling, or discovery is a neuron instead.

### 2.7 Integrations

Integration modules expose typed neurons:

```csharp
DigitalBrain.Google.IGmail
DigitalBrain.Google.ICalendar
DigitalBrain.Salesforce.ISalesforce
```

These interfaces are semantic capabilities, not MCP toolsets. `IGmail` means Gmail behavior;
`ISalesforce` means Salesforce behavior. Official MCP clients, OAuth, token refresh, transport
schemas, reconnection, schema filtering, and invocation stay inside the owning module. Raw MCP
clients, tool names, protocol DTOs, and tool dictionaries never cross the module interface.

Each integration module owns its Aspire hosting package. That package declares required OAuth
parameters, secret descriptions, callback/resource references, and any official MCP process or
endpoint. The silo still calls only `AddDigitalBrain()`.

Google and Salesforce do not depend on AI. Application agents compose integration neurons with concrete LLM neurons.

When AI uses integration capabilities as tools, MAF middleware is the enforcement point:

- The integration module classifies operations as safe read-only, mutating, or unknown.
- Safe read-only operations may be automatically approved.
- Mutating and unknown operations require human approval.
- An approver agent may advise but never owns authority.
- Behaviors reference semantic capabilities and never raw MCP tools.

Tool availability is based on the complete pinned, authenticated, and granted capability catalog.
There is no fixed tool-count limit. An MAF context provider injects all exact schemas when they fit
the token budget; otherwise it performs hybrid retrieval. Previously used tools remain sticky in the
session. Summaries and embeddings are disposable indexes only; invocation always uses the exact
current schema.

Every tool-enabled agent has a safe `FindCapabilityTools` recovery function. A miss may retrieve and
add only previously unseen tools from the pinned and granted catalog, then rerun with finite progress.
There is no generic raw invoke escape hatch.

### 2.8 Canonical registry and semantic discovery

The generated catalog is the canonical registry. Its entries derive from:

- Public namespace and contract type name
- XML documentation
- Method names and parameter types
- Handled and emitted synapse types
- Owning module

Future natural-language programming follows this path:

```text
"Ask Google Calendar for tomorrow's events"
                      ↓
derived vector search over the generated catalog
                      ↓
DigitalBrain.Google.ICalendar
                      ↓
exact typed neuron proxy
```

Vector search may rank candidates. It may never execute an invented type or bypass exact catalog resolution.

### 2.9 Behaviors and scripting

A working C# file creates live behavior by composing existing typed vocabulary. It does not invent
new public neuron contracts at runtime.

Each working file contains exactly one public `Behavior` class. The behavior compiler is
contract-only:

- Allowed: the Behavior API, `DigitalBrain.Abstractions`, selected module contracts, approved BCL
  types, and MEAI message types.
- Forbidden: `IGrainFactory`, `IChatClient`, provider SDKs, MCP protocol types, `HttpClient`,
  `IServiceProvider`, filesystem/process APIs, and reflection.

Behaviors are activated by existing typed synapses, may make existing typed method requests, and emit
existing typed synapses. Installation remains a human-approved, journaled, and reversible proposal.

Dynamic agents are allowed only as behavior-scoped MAF `AIAgent` instances composed from existing
contracts. A behavior may supply a dynamic prompt or persona. It may not introduce dynamic
capabilities, bypass the typed registry, or register the temporary agent as a public neuron.

### 2.10 MAF state, durability, compaction, and diagnostics

Orleans is the durability authority around MAF:

- A completed conversational turn is stored in the MAF `AgentSession`.
- An unfinished current turn may additionally store the latest standard MAF workflow checkpoint.
- The MAF Durable Extension is rejected because it would duplicate Orleans durability.
- Synapse journals remain the durable causal truth.
- OpenTelemetry traces, metrics, and logs are diagnostic projections and never the audit source.

Persisted AI state uses a versioned envelope containing:

- DigitalBrain state format version.
- MAF version.
- Definition fingerprint.
- Typed participant identities.
- Serialized `AgentSession`.
- Optional current workflow checkpoint.

Restore reconstructs the exact composed agent/workflow and then restores state. A change to
participants, prompts, providers, tools, orchestration definition, or MAF version cannot silently
reuse old state. The old state is preserved and a migration-required fact is emitted; reset or
migration is explicit. Persisted state is encrypted and protected as sensitive data.

MAF compaction is an internal implementation detail:

- It is token-budget driven, never a fixed message count.
- Old tool results collapse first.
- The same typed participant model summarizes its own history.
- Truncation is emergency fallback only.
- Group participants compact with their own typed models.
- Compacted state remains inside the same session.
- Experimental MAF compaction types never leak into public contracts.

Telemetry forms one correlated chain:

```text
Kernel synapse span
  -> MAF workflow and agent spans
     -> model-client and capability spans
```

Spans carry `db.owner`, `db.neuron`, `db.synapse.id`, `db.synapse.type`, `db.correlation`, and
`db.causation` attributes. Sensitive content is off by default. Aspire receives OTLP. A future
Memory module may project journals but must never reconstruct truth by scraping traces.

### 2.11 Tasks

`DigitalBrain.Tasks` is an independent module family. A Task is durable domain identity for a desired
outcome; a MAF Workflow is how one execution Attempt runs. A Task survives worker, model,
orchestration, and deployment changes.

The dependency direction is:

```text
DigitalBrain.Modules.Tasks.Contracts -> DigitalBrain.Abstractions
DigitalBrain.Modules.Tasks           -> Tasks.Contracts + Kernel
DigitalBrain.Modules.AI.Contracts    -> Tasks.Contracts
DigitalBrain.Modules.AI              -> AI.Contracts + MAF
```

Tasks knows nothing about AI, MAF, models, prompts, executors, sessions, or checkpoints.

The contracts package owns `ITask`, `IWorker`, `AttemptId`, `AttemptRequest`, `AttemptCursor`, typed
task/attempt facts, and typed blockers. The runtime owns an internal `TaskNeuron : Neuron, ITask`.
`IGroupChat` is both an `IAgent` and an `IWorker`. Only session-owning AI orchestration neurons
implement `IWorker`; ordinary stateless agents and raw LLMs do not. A single-agent hard task uses a
one-participant `Sequential` worker.

Worker requests are short, idempotent interface methods:

```csharp
Task AcceptAsync(AttemptRequest request);
Task ContinueAsync(AttemptCursor cursor);
Task CancelAsync(AttemptCursor cursor);
```

They validate, persist, schedule an internal turn, and return. They never execute a long MAF
superstep inline.

Workers report typed facts:

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

Every fact identifies the Task neuron, Worker neuron, Attempt, and Revision. The Task accepts a fact
only when task, worker, attempt, revision, and `SynapseDelivery.Caller` all match.

Revision fencing is strict:

```text
Accept attempt revision 0
Worker emits AttemptAdvanced(0)
Task requests Continue(1)
```

Older revisions are stale and ignored. Future revisions indicate corruption and are rejected.
Terminal attempts reject continuation. A retry always receives a new `AttemptId`.

Exactly one Attempt may be active for a Task. Parallel thinking belongs inside that Attempt through
MAF `Concurrent`, group chat, or workflow branches. Deliberately competing solutions are child Tasks
coordinated by a parent Task, not racing attempts against the same Task.

An Attempt failure is not automatically a Task failure. Task policy may start another sequential
Attempt, enter `Waiting`, or declare terminal failure. Task terminal states are immutable:
`Succeeded`, `Failed`, and `Cancelled`. A later retry is a successor Task linked by `RetryOf`.

The Task lifecycle is deliberately small:

```text
Pending -> Running <-> Waiting
Running/Waiting -> Cancelling -> Cancelled
Running/Waiting -> Succeeded
Running/Waiting -> Failed
```

`Waiting` carries a generic typed blocker rather than AI-specific state:

```text
InputRequired
ApprovalRequired
DependencyPending
RetryScheduled
OutcomeUncertain
```

The worker retains detailed MAF or integration state. The Task knows blocker identity, category,
revision, and resolution only.

Cancellation is best-effort intent, never pretend rollback. While `Cancelling`, the worker may
truthfully report cancellation, success that won the race, failure, or uncertain outcome. Completed
Gmail or Salesforce effects are not called cancelled; compensation is an explicit capability or
successor Task.

Task journals and typed facts replace the old IAW `TaskLedgerGrain`. There is no generic string
`TaskEvent`, prompt-formatted persistence, destructive clear operation, or duplicated per-agent
ledger. An AI context provider may project or summarize typed task facts without becoming their
authority.

MAF Harness todo/planning state remains private agent planning. It is not promoted to a domain Task
unless the system deliberately creates an `ITask` identity.

### 2.12 AI workers and the fenced runner

The object ownership is:

```text
TaskNeuron
  -> task-scoped orchestration neuron for Attempt N
     -> persisted MAF session/checkpoint/fingerprint
        -> reconstructed MAF Workflow
           -> private MAF executors
              -> typed ILLM and IAgent neurons
```

Each Attempt receives a distinct task-scoped orchestration identity, such as
`task-name/attempt-N`. `TaskNeuron` owns task state and attempt references. The orchestration neuron
owns MAF session/checkpoint state. Private MAF executors own neither.

A worker neuron must not execute a long MAF superstep inside its serialized Orleans turn. It
persists a fenced execution lease containing Attempt, Revision, workflow fingerprint, and deadline,
then returns. An internal AI runner executes the superstep and returns checkpoint/session state plus
the outcome. The worker atomically accepts only its active lease. Cancellation revokes the lease;
late or duplicate results cannot overwrite newer state. Reminders may redispatch an unfinished
lease after a crash.

The runner is infrastructure, not a public neuron: it has no registry entry, journal, semantic
interface, scripting visibility, or durable domain identity.

MAF `RunStatus` is not a Task state. Running means the Attempt is executing; an idle checkpoint is
not completion; pending MAF requests map to Task `Waiting`; output may complete an Attempt; error
feeds Task retry/failure policy.

### 2.13 Time

Time is an independent module family with public vocabulary in `DigitalBrain.Time`. Public scheduled
behavior and private Kernel scheduling are separate:

- Kernel timers/reminders maintain outbox delivery, lease recovery, and retry pumps.
- Time neurons represent schedules that behaviors, Tasks, and modules may address.
- Behaviors never see Orleans `IGrainTimer`, `IGrainReminder`, `TickStatus`, or reminder names.
- Kernel-owned reminder names use a reserved `db.*` namespace.

The public one-shot duration capability is `DigitalBrain.Time.ICountdown`, not `ITimer`.
`.NET 10` already defines `System.Threading.ITimer`, and this repository enables implicit usings.
`DigitalBrain.Time.IReminder` represents absolute or recurring schedules.

Both are durable semantic neurons independent of the Orleans primitive names:

- `ICountdown` is a one-shot duration.
- `IReminder` is an absolute or recurring schedule.
- Both survive deactivation and silo failure.
- Neither promises real-time precision: an occurrence is never intentionally early and is eventually
  observed after its due time.
- Each logical schedule has exactly one neuron identity, lifecycle, current revision, and explicit
  owner-bound destination `NeuronId`.
- The registry indexes the contracts, not every runtime schedule instance.
- High-frequency internal retries remain private infrastructure.

Scheduling uses strict lifecycle requests:

```text
Start/Schedule     only from Unscheduled
Reschedule         only from Scheduled with ExpectedRevision
Cancel             only from Scheduled with ExpectedRevision
Restart            explicitly begins a new generation after Elapsed/Cancelled
Read               returns the current snapshot
```

Each mutation includes a stable `CommandId`. Repeating a command returns its recorded result.
Stale revisions do not mutate state; future revisions indicate corruption. State transitions emit
typed scheduled/rescheduled/cancelled facts instead of living only in opaque Orleans state.

Elapsed facts are directed to the explicit destination and carry schedule identity, revision,
scheduled time, observed time, and resolution metadata. “Who configured this?” and “who receives
the occurrence?” remain separate concepts. Cross-owner delivery requires a future explicit grant.

Public Time-neuron state is authoritative; the internal Orleans adapter is only a wake-up mechanism.
Start/reschedule registers a revision-fenced wake-up before committing the new schedule; an
uncommitted callback is ignored. Cancellation commits the new revision before unregistering; a late
callback is ignored. Adapter callbacks contain only schedule identity, revision, and occurrence
identity—never arbitrary actions or stored synapse payloads.

The current `Neuron.ReceiveReminder(...)` implementation treats every reminder name as the private
outbox wake-up. That behavior is not an extension point and must be removed from the public neuron
surface rather than inherited by modules.

Production combines an activation-local Orleans timer for latency with a durable Orleans reminder
as backstop. Both may race; the Time neuron deduplicates by revision and occurrence. Tests use
`TimeProvider` as the sole clock plus a deterministic schedule driver so simulations advance time
without wall-clock waiting.

Recurring schedules distinguish:

- `IntervalSchedule`: elapsed duration anchored to an instant.
- `CalendarSchedule`: wall-clock recurrence in an IANA time zone.

For civil-time DST gaps, an occurrence moves to the first valid instant after the gap. For overlaps,
it occurs once at the earlier instant. Facts preserve the requested local time, resolved instant,
offset, and adjustment.

An overdue one-shot occurs once after recovery. Recurring missed occurrences coalesce into one
`ReminderOverdue` fact containing first missed time, last missed time, missed count, recovery time,
and revision; the schedule then advances to the first future occurrence. A Reminder is a wake-up,
not a durable job queue. Work requiring every occurrence becomes Tasks.

Kernel owns one shared durable Orleans reminder provider because the outbox requires it even without
the Time module. Time reuses that provider. In-memory reminders are explicitly development/testing
only and production rejects them.

The first durable Aspire profile is:

```csharp
var brain = builder
    .AddBrain("brain")
    .WithAzureStorage();
```

One Azure Storage resource supplies Blob-backed neuron journals and Table-backed Orleans clustering
and reminders. Local development uses Azurite; deployment uses real Azure Storage.
`WithDevelopmentStores()` remains explicitly non-durable. No generic durability-provider
abstraction is introduced until another complete journaling, clustering, and reminder profile
exists.

### 2.14 Decisions still open

The following were discussed but not approved and must not be implemented as settled architecture:

- The exact supervised MAF execution mode and whether every worker turn advances exactly one
  Lockstep superstep.
- The durable capability-invocation ledger, provider idempotency, reconciliation, and uncertain
  external-side-effect protocol.
- Exact Task request/result payload types, retry policy configuration, timeouts, and result/evidence
  representation.
- Exact public Time record shapes and the internal calendar recurrence library.
- The complete capability-request caller/causation envelope and target-side request journaling.
- Memory architecture. It remains out of scope.

Evidence retained for the next grilling pass:

- A throwaway prototype against `Microsoft.Agents.AI.Workflows` 1.13.0 showed that cancelling
  OffThread execution on the first `SuperStepCompletedEvent` was too late: later executors had
  already run and resume produced counts `first=1`, `second=2`, `third=3`. The equivalent Lockstep
  prototype advanced and resumed with all three counts equal to one. This evidence motivates, but
  does not yet ratify, one-superstep worker turns.
- Orleans persists reminder definitions but not individual occurrences; cluster downtime can miss a
  tick. This is why DigitalBrain coalesces overdue recurrence explicitly.
- The old IAW `TaskLedgerGrain` has no production append callers in the inspected snapshot, mixes
  prompt formatting into persistence, duplicates other logs, and permits destructive clearing. Only
  its durable-hard-work intent survives.

## 3. Hard deletion manifest

Delete these concepts without shims:

```text
ModelTier
ModelProviders
IModelCompletionService
Neuron.AskModelAsync
BrainService.WithModel
ModelDescriptor
ModelCatalog
ProviderFactory
AddDigitalBrainModels
AddAIModule
ChatModelNeuron
IChatModel
ScriptedModelCompletion
Models.feature
the duplicate BrainClient interface
the Probe module/template
the unused connection lifecycle scaffold
IAnswer
```

Delete or rewrite every test, sample, host, public API baseline entry, and documentation page whose only purpose is to preserve those concepts.

Delete the superseded planning stack after this file becomes canonical:

```text
ARCHITECTURE-REVIEW.md
PLAN.md
GOAL.md
```

Git history is the archive. Contradictory live plans are not documentation.

## 4. Implementation plan

### Task 1: Prove the rejected architecture is gone

**Files:**

- Create: `tests/DigitalBrain.Tests/ArchitectureCutContracts.cs`
- Delete: `tests/DigitalBrain.Tests/ProviderAdapterContracts.cs`
- Delete: `tests/DigitalBrain.Simulations/Models.feature`

**Interfaces:**

- Consumes: compiled framework assemblies and the repository project graph
- Produces: an executable deletion gate for forbidden types, methods, package references, and registration names

- [x] Write a test asserting Kernel exposes no method containing `Model`, no framework assembly defines `ModelTier`, and Kernel reaches no AI SDK.
- [x] Write a repository search test rejecting the exact legacy identifiers in production `.cs` and `.csproj` files.
- [x] Run `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`; verify it fails against the existing tier architecture.
- [x] Perform the deletion slice.
- [x] Run the owning tests and the root gate.

### Task 2: Replace the provisional module descriptor

**Files:**

- Modify: `src/DigitalBrain.Abstractions/IModule.cs`
- Delete: `src/DigitalBrain.Kernel/ModuleComposition.cs`
- Delete: `src/DigitalBrain.Kernel/ModuleWiring.cs`
- Modify: `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs`
- Modify: `src/DigitalBrain.Kernel/DigitalBrainSiloBuilderExtensions.cs`
- Modify: `tests/DigitalBrain.Tests/ModuleContracts.cs`

**Interfaces:**

- Consumes: canonical `IModule` marker types and referenced assemblies at compilation
- Produces: generated `AddDigitalBrain()` composition with the available module catalog

- [x] Write a generated-catalog test proving the test assembly sees `AIModule` from its project reference.
- [x] Verify the test fails because no generated module catalog exists.
- [x] Reduce `IModule` to a marker.
- [x] Generate the `AddDigitalBrain()` extension in the consuming compilation.
- [x] Move Kernel’s fixed runtime setup behind the generated extension.
- [x] Validate AppHost-selected module names against generated available module names at startup.
- [x] Run generator tests, the owning test project, and the root gate.

### Task 3: Build typed AI contracts and runtime

**Files:**

- Replace: `modules/DigitalBrain.Modules.AI.Contracts/IChatModel.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/ILLM.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/IAgent.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/IGroupChat.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/Ollama/ILlama32.cs`
- Create: `modules/DigitalBrain.Modules.AI.Contracts/OpenAI/IGpt56.cs`
- Replace: `modules/DigitalBrain.Modules.AI/AIModule.cs`
- Delete: `modules/DigitalBrain.Modules.AI/ModelBinding.cs`
- Delete: `modules/DigitalBrain.Modules.AI/ModelConfiguration.cs`
- Create: `modules/DigitalBrain.Modules.AI/LLM.cs`
- Create: `modules/DigitalBrain.Modules.AI/LlmAttribute.cs`
- Create: `modules/DigitalBrain.Modules.AI/Ollama/Llama32.cs`
- Create: `modules/DigitalBrain.Modules.AI/OpenAI/Gpt56.cs`
- Modify: both AI project files

**Interfaces:**

- Consumes: `INeuron`, Kernel `Neuron`, and provider `IChatClient` implementations
- Produces: `ILLM`, `ILlama32`, `IGpt56`, `IAgent`, `IGroupChat`, `LLM`, and `[Llm<TModel>]`

- [x] Write a test that constructs `Llama32` from an `IChatClient` keyed by `typeof(Llama32)`.
- [x] Verify it fails because the typed model and key attribute do not exist.
- [x] Implement the contracts and base `LLM`.
- [x] Implement convention-driven OpenAI and Ollama client registration keyed by the concrete model type.
- [x] Add an architecture test rejecting `IChatClient` constructor injection outside concrete `LLM` subclasses.
- [x] Run AI tests, package guards, and the root gate.

### Task 4: Give AI its own Aspire hosting package

**Files:**

- Create: `modules/DigitalBrain.Modules.AI.Aspire.Hosting/DigitalBrain.Modules.AI.Aspire.Hosting.csproj`
- Create: `modules/DigitalBrain.Modules.AI.Aspire.Hosting/AIHostingExtensions.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/BrainHosting.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `Directory.Packages.props`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: `BrainService`, `AIModule`, concrete model types, Aspire OpenAI, and Aspire Ollama resources
- Produces: `brain.AddModule<AIModule>(ai => ai.WithLlm<TModel>())`

- [x] Write AppHost model tests for one module declaration, duplicate rejection, shared OpenAI parameter, Markdown description, and no Ollama secret.
- [x] Verify those tests fail against `WithModel`.
- [x] Implement generic module selection in core Aspire hosting.
- [x] Implement AI’s `WithLlm<TModel>()` convention and resources.
- [x] Project only parameter/resource expressions into the silo.
- [x] Prove publish output contains no secret literal.
- [x] Run hosting tests, publish-manifest tests, and the root gate.

### Task 5: Remove the duplicate client and model test path

**Files:**

- Delete: `src/DigitalBrain.Client/BrainClient.cs`
- Modify: `src/DigitalBrain.Client/DigitalBrainClient.cs`
- Modify: `src/DigitalBrain.Aspire/BrainClientIntegration.cs`
- Modify: `src/DigitalBrain.Testing/Simulation.cs`
- Modify: `src/DigitalBrain.Testing/SimulationCluster.cs`
- Modify: `src/DigitalBrain.Testing/NeuronSteps.cs`
- Delete: `src/DigitalBrain.Testing/ScriptedModel.cs`
- Delete: `src/DigitalBrain.Testing/ScriptedModelCompletion.cs`
- Modify: `hosts/DigitalBrain.ProbeHost/Neurons.cs`
- Modify: `hosts/DigitalBrain.ProbeHost/Program.cs`
- Delete: `tests/DigitalBrain.Simulations/ThinkerNeuron.cs`

**Interfaces:**

- Consumes: owner-bound Orleans client and session neuron
- Produces: one `DigitalBrainClient` programming interface

- [x] Change client contract tests to require one public client type.
- [x] Verify they fail while `BrainClient` exists.
- [x] Move still-consumed observation support behind `DigitalBrainClient` or testing-only helpers.
- [x] Migrate hosts, samples, and simulations.
- [x] Delete the tier-driven scripted model path.
- [x] Run simulations, host tests, and the root gate.

### Task 6: Delete provisional modules and stale documents

**Files:**

- Delete: `modules/DigitalBrain.Modules.Probe.Contracts/**`
- Delete: `modules/DigitalBrain.Modules.Probe/**`
- Delete: `src/DigitalBrain.Abstractions/ConnectionHealth.cs`
- Delete: `src/DigitalBrain.Kernel/ConnectionLifecycle.cs`
- Delete: `tests/DigitalBrain.Tests/ConnectionHealthContracts.cs`
- Delete: `ARCHITECTURE-REVIEW.md`
- Delete: `PLAN.md`
- Delete: `GOAL.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `website/architecture.md`
- Modify: `website/index.md`
- Modify: `website/quickstart.md`
- Modify: `website/status.md`
- Modify: `website/packages/abstractions.md`
- Modify: `website/packages/aspire-hosting.md`
- Modify: `website/packages/client.md`
- Modify: `website/packages/kernel.md`
- Modify: public API baseline files

**Interfaces:**

- Consumes: the refined architecture in this file
- Produces: one live plan and documentation matching the compiled public surface

- [x] Remove no-consumer scaffolding and its self-referential tests.
- [x] Point `CLAUDE.md` to this file as the plan of record.
- [x] Rewrite the README quickstart around AppHost module selection and `silo.AddDigitalBrain()`.
- [x] Remove all website claims about tiers, `AskModelAsync`, and `BrainClient`.
- [x] Regenerate or edit public API baselines to the compiled surface.
- [x] Run `node tools/render-specification.mjs`.
- [x] Run `node --test tests/*.test.mjs` from `website/`.
- [x] Run the root gate.

### Ratified continuation, not yet an executable checklist

The next implementation plan must be cut from the ratified sections above only after the remaining
questions in section 2.14 are resolved. It must cover, in dependency order:

1. Replace the provisional string AI exchange with the MEAI message/response boundary.
2. Introduce `DigitalBrain.Tasks.Contracts` and the Task lifecycle without any AI dependency.
3. Compose MAF-backed typed Agents and orchestration neurons.
4. Implement `IWorker` in session-owning AI orchestrations and add the fenced runner.
5. Implement behavior compilation and dynamic behavior-scoped agents against contract-only
   references.
6. Implement semantic capability/MCP adaptation, retrieval, and approval middleware.
7. Introduce `DigitalBrain.Time` with `ICountdown`, `IReminder`, deterministic simulation time, and
   shared durable reminder hosting.
8. Continue with the generated semantic registry and integration modules.

This list records scope and dependencies, not permission to fill unresolved details with guesses.

## 5. Acceptance gates

The hard cut is complete only when all commands are fresh and green:

```powershell
rg -n "ModelTier|ModelProviders|IModelCompletionService|AskModelAsync|WithModel\(|AddAIModule|AddDigitalBrainModels|ChatModelNeuron|class BrainClient" src modules hosts samples tests website
```

Expected: no matches.

```powershell
dotnet test --logger "console;verbosity=minimal"
```

Expected: zero failures and zero skips.

```powershell
Set-Location website
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

Expected: rendering succeeds and all website tests pass.

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only intentional architecture changes before commit and a clean tree after commit.

## 6. Deferred in dependency order

After the ratified AI, Tasks, behavior, integration, and Time foundations are proven:

1. Complete owner-safe client scripting and the proposal/approval/install/rollback rail.
2. Generate the canonical neuron catalog from public contracts and method/synapse vocabulary.
3. Add semantic/vector discovery as a disposable index over that catalog.
4. Add `DigitalBrain.Google` with typed `IGmail` and `ICalendar` neurons over official MCP.
5. Add `DigitalBrain.Salesforce` with typed `ISalesforce` over official MCP.
6. Add `DigitalBrain.Flutter` containing only Flutter neurons and its contract drift guard.
7. Design `DigitalBrain.Memory` independently around its own vocabulary. Do not infer its
   architecture from AI, Tasks, or Time.

No deferred item justifies retaining a rejected abstraction today.
