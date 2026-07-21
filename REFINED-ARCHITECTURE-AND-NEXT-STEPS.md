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

Typed interface requests are reified as causal facts without turning the request itself into a
synapse:

1. Before invocation, the caller creates and commits `CapabilityRequested : Synapse`.
2. Its `SynapseDelivery` is propagated through Orleans `RequestContext`.
3. The target commits the same delivery to its incoming journal before invoking the method.
4. The target executes with that delivery as its current causal context.
5. Emitted synapses inherit the correlation and use the request fact's `SynapseId` as causation.
6. `CapabilityCompleted`, `CapabilityFailed`, or `CapabilityRejected` records the outcome.

Generic capability facts carry identity, caller, target, contract, method, correlation, causation,
timestamp, and outcome only. They do not copy arguments, prompts, secrets, tokens, return values, or
exception content into Kernel journals. Modules emit typed facts when payload-level audit is
required.

This protocol records attempted, accepted, completed, failed, rejected, and visibly incomplete
requests. It does not claim exactly-once RPC. Domain `CommandId`, revision fencing, provider
idempotency, and reconciliation remain responsible for safe retries. The provisional
`CapabilityCall` name is replaced by `CapabilityRequested`.

A private off-turn runner crosses the Kernel/AI assembly boundary with the single deliberately
public `DigitalBrain.Kernel.CapabilityDelegation` transport. It is sealed, opaque,
non-constructible by consumers, hidden from IntelliSense, and non-semantic. Kernel alone mints,
carries, validates, durably redeems, and records outcomes for it. `CausalCaller` is the GroupChat
whose outgoing journal owns the precommitted request; `DelegateSource` is the private runner
`GrainId` the filters physically observe. The delegation binds those identities plus owner, exact
target, contract and method, correlation/causation, and opaque one-use identity—nothing from Tasks,
AI, MAF, checkpoints, approvals, integrations, leases, generations, or renewals.

Every off-turn participant or integration call receives its own precommitted request and
delegation. The initiating Task-to-worker request does not authorize later runner calls. A raw
non-neuron call, forged context, replay, or mismatched source/owner/target/operation is rejected
before semantic method entry. Consumption is durable before invocation; a crash may require a new
request and delegation because the cross-grain boundary is not exactly once.

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

Only state-owning orchestration neurons are conversationally stateful. The direct `RespondAsync`
entry path owns one protected serialized MAF `AgentSession`. A supervised `IWorker` Attempt owns one
raw MAF workflow checkpoint lineage instead; participant sessions remain encapsulated inside that
checkpoint. DigitalBrain must not keep a second transcript, extract a participant session, or add a
parallel outer `AgentSession` to the supervised path. MAF 1.13 exposes no supported public bridge
between these artifacts, so neither path implicitly seeds the other.

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

For the Foundation PoC, `IGmail` and `ISalesforce` are capability-root identities rather than
hand-written CRUD facades. Their pinned, authenticated, and granted tool catalogs stay
module-private. AI obtains transient exact tools from a provider-neutral runtime seam implemented by
Google, Salesforce, and test adapters. That seam is module-author infrastructure: contract packages,
behaviors, natural-language discovery, and the model cannot see or invoke it directly. The model sees
only selected exact `AIFunction` schemas.

Every selected tool still routes through the semantic integration neuron. The integration neuron
therefore remains the owner of authorization, incoming request journals, approval validation,
`CommandId`, mutation state, and reconciliation. A public high-level method or typed request is added
to `IGmail` or `ISalesforce` only when a real deterministic non-agent caller requires it. MCP tool
names and JSON never become permanent public domain vocabulary merely because an MCP server exposes
them.

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

External mutations use an integration-owned durable command protocol. Every mutation carries
`DigitalBrain.Abstractions.CommandId` and a canonical payload fingerprint:

```text
Proposed
  -> AwaitingApproval
  -> Approved
  -> Invoking
  -> Completed
             \-> OutcomeUncertain
```

The same CommandId and fingerprint resumes or returns the recorded result. Reusing an ID with
different content is rejected. Human approval is bound to the exact fingerprint. MAF middleware
coordinates the pause and resume, while the integration neuron independently validates the durable
approval so typed callers cannot bypass it.

The integration commits `Invoking` before contacting MCP and passes CommandId as the provider
idempotency key when supported. After a crash in `Invoking`, it reconciles by reading provider state
before considering another mutation. Proven state becomes `Completed`; an unprovable outcome becomes
`OutcomeUncertain` and the Task waits. An uncertain mutation is never blindly repeated.

The command ledger lives in the integration neuron's durable state and typed journal, not a new
public service. Read-only operations remain safely retryable and do not require the mutation ledger.
DigitalBrain never claims exactly-once external effects.

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

Each working file contains exactly one public `Behavior` class. When the behavior compiler is
introduced, it is contract-only:

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

Orleans is the durability authority around MAF, with one outer artifact per entry path:

- A direct conversational turn is stored in a protected MAF `AgentSession` envelope.
- A supervised Attempt stores the latest standard MAF workflow checkpoint lineage instead.
- The supervised checkpoint may contain MAF-owned participant sessions internally; DigitalBrain does
  not extract them or persist another outer session.
- The MAF Durable Extension is rejected because it would duplicate Orleans durability.
- Synapse journals remain the durable causal truth.
- OpenTelemetry traces, metrics, and logs are diagnostic projections and never the audit source.

Both path-specific envelopes contain:

- DigitalBrain state format version.
- MAF version.
- Definition fingerprint.
- Typed participant identities.

The direct envelope then contains a serialized `AgentSession`. The supervised envelope instead
contains replayable initial input and a durable checkpoint reference. Definition compatibility is
validated before invoking MAF. A checkpoint-store identity is stable for Worker + Task + Attempt;
redispatch creates a new `RunId`, not a new checkpoint lineage.

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

A Task owns one immutable, typed `Goal`. Tasks defines only the extension vocabulary:

```csharp
public abstract record Goal;
public abstract record Result;
public abstract record Failure;

public readonly record struct FactReference(
    NeuronId Source,
    SynapseId Fact);

public sealed record TaskPolicy(
    int MaximumAttempts,
    TimeSpan RetryDelay,
    DateTimeOffset? Deadline);
```

Modules and applications define concrete Goals, Results, and Failures. Tasks contains no `object`,
arbitrary JSON, metadata dictionaries, generic event strings, or AI prompts. `GroupChat` maps those
application-owned types through two deterministic, synchronous protected hooks:

```csharp
protected abstract IReadOnlyList<ChatMessage> CreateMessages(Goal goal);
protected abstract Result CreateResult(IReadOnlyList<ChatMessage> messages);
```

The base class copies input before MAF receives it and exposes terminal workflow output as a
read-only message list. It does not add an AI-owned Goal/Result hierarchy, a generic
`GroupChat<TGoal, TResult>`, public mapper interface, reflection convention, or service-locator seam.

Every Attempt receives the immutable Goal. Success returns one typed Result plus references to
supporting facts. The Task copies the accepted Result and evidence references into its immutable
terminal journal without duplicating evidence payloads.

Failures are typed and classified as retryable or permanent. Retryable failures may create a new
Attempt while `MaximumAttempts` and the optional absolute `Deadline` permit it. A fixed
`RetryDelay` is sufficient for the PoC and uses private durable reminders, so Tasks does not depend
on Time. `AttemptOutcomeUncertain` is never retried automatically.

There is no public per-Attempt timeout. The internal `WorkflowRun.RecoverAfter` detects a runner
whose result has not been adopted.
Task deadline expiry requests cancellation; terminal state follows the observed outcome and records
a typed deadline failure when work did not complete.

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

### 2.12 AI workers and the recoverable runner

The object ownership is:

```text
TaskNeuron
  -> task-scoped orchestration neuron for Attempt N
     -> persisted replay input/checkpoint/fingerprint + at most one ActiveRun
         -> reconstructed MAF Workflow
            -> private MAF executors
               -> typed ILLM and IAgent neurons
```

Each Attempt receives a distinct task-scoped orchestration identity, such as
`task-name/attempt-N`. `TaskNeuron` owns task state and attempt references. The orchestration neuron
owns the supervised MAF checkpoint lineage. Private MAF executors own neither. Direct chat uses a
separate protected session envelope; `RespondAsync` is rejected while a supervised run is active so
the paths cannot mutate orchestration state concurrently.

A worker neuron must not execute a long MAF superstep inside its serialized Orleans turn. It
persists replayable initial input, the initiating durable causal reference, and one active
`WorkflowRun` containing a fresh `RunId`, full `AttemptCursor`, definition fingerprint, input
checkpoint reference, and `RecoverAfter`, then returns. An internal AI runner executes the
superstep and returns the exact input lineage plus the new checkpoint and terminal/pending output.
The worker accepts a result only when its `RunId`, cursor, fingerprint, and input checkpoint match
the current `ActiveRun`, and only after the checkpoint store has committed the returned checkpoint.
Recovery replaces the `RunId` while retaining the stable Worker + Task + Attempt checkpoint-store
identity. Cancellation clears `ActiveRun`; serialized turn ordering determines whether an already
committed success or cancellation wins, and all later results are stale.

Each supervised `WorkflowRun` advances exactly one MAF Lockstep superstep. The runner restores the
input checkpoint, consumes events through the first `SuperStepCompletedEvent`, retains that event's
checkpoint, and breaks immediately. In the pinned GroupChat path, canonical terminal output is
emitted before the final superstep-completion event; requesting another event after completion can
execute the next Lockstep superstep. A nonterminal checkpoint resumes its already queued messages
without another `TurnToken`, then returns control for durable checkpoint adoption before another run
may continue. MAF `Concurrent` may still run participants in parallel inside that superstep. There
is no exactly-once claim across a crash after checkpoint-store commit but before worker adoption.

The runner is infrastructure, not a public neuron: it has no registry entry, journal, semantic
interface, scripting visibility, or durable domain identity. The initiating Task-to-worker request
is journaled before the runner starts, but it cannot authorize later off-turn calls. Before each
typed participant or integration call, the GroupChat commits a distinct exact
`CapabilityRequested` and Kernel mints the opaque-public, non-semantic `CapabilityDelegation` for
the actual runner source. The Kernel token carries no run, Attempt, Task revision, checkpoint, MAF,
approval, integration, lease, generation, or renewal state; each owning module validates those
semantics independently. A broad Kernel bypass remains forbidden.

MAF `RunStatus` is not a Task state. Running means the Attempt is executing; an idle checkpoint is
not completion; pending MAF requests map to Task `Waiting`; output may complete an Attempt; error
feeds Task retry/failure policy.

### 2.13 Time

Time is an independent module family with public vocabulary in `DigitalBrain.Time`. Public scheduled
behavior and private Kernel scheduling are separate:

- Kernel timers/reminders maintain outbox delivery, run recovery, and retry pumps.
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

- The internal calendar recurrence library and exact recurring/calendar record shapes.
- Memory architecture. It remains out of scope.

The one-shot `ICountdown`/`IReminder`, Tasks, AI, integration-root, and capability-tool seams proposed
for the Foundation PoC are written explicitly in
`docs/superpowers/plans/2026-07-20-foundation-poc.md`. Approval of that plan freezes those PoC
contracts. It does not approve recurring schedules or Memory.

Evidence retained for the next grilling pass:

- A throwaway prototype against `Microsoft.Agents.AI.Workflows` 1.13.0 showed that cancelling
  OffThread execution on the first `SuperStepCompletedEvent` was too late: later executors had
  already run and resume produced counts `first=1`, `second=2`, `third=3`. The equivalent Lockstep
  prototype advanced and resumed with all three counts equal to one. This evidence supports the
  ratified one-superstep worker boundary.
- Orleans persists reminder definitions but not individual occurrences; cluster downtime can miss a
  tick. This is why DigitalBrain coalesces overdue recurrence explicitly.
- The old IAW `TaskLedgerGrain` has no production append callers in the inspected snapshot, mixes
  prompt formatting into persistence, duplicates other logs, and permits destructive clearing. Only
  its durable-hard-work intent survives.

### 2.15 Foundation PoC boundary

The first executable proof is one vertical scenario across exactly five module families: Tasks,
Time, AI, Google, and Salesforce.

```text
Create durable Task
  -> read Gmail through official MCP
  -> ask two typed LLMs independently
  -> reconcile them through MAF group orchestration
  -> survive a silo restart between Lockstep supersteps
  -> request human approval
  -> update one Salesforce record through official MCP
  -> schedule a follow-up Countdown or one-shot Reminder
  -> complete with typed result/evidence and causal journals
```

The PoC includes one Gmail read capability, one Salesforce mutation, one Countdown, and one one-shot
Reminder. Automated tests replace external systems only at the MCP boundary; credentialed live
smoke tests are optional.

The PoC explicitly excludes Memory, Flutter, model tiers/routing/balancing/fallback, broad MCP tool
coverage, recurring calendar rules, and runtime Behavior installation. It uses ordinary compiled
typed C# composition. This proves the durable module architecture, not the later self-programming
governance rail.

The exact minimal contracts, runtime seams, proof order, and stop conditions are now proposed in
`docs/superpowers/plans/2026-07-20-foundation-poc.md`. Plan approval is the final architecture gate.
After approval, grilling continues only as a diff/proof discipline at green slice boundaries; it
does not reopen speculative architecture.
Implementation proceeds as red-green vertical slices; no new abstraction is introduced unless a
failing behavioral proof requires it.

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

### Foundation PoC execution plan

The completed hard-cut tasks above remain historical proof. The next executable work is the
file-level, red-green plan in `docs/superpowers/plans/2026-07-20-foundation-poc.md`. It contains:

1. The causal capability-request repair.
2. Independent Tasks contracts and lifecycle.
3. The MEAI public wire and MAF-backed typed Agents.
4. Concurrent/group orchestration with one serialized MAF session.
5. The fenced one-Lockstep-superstep `IWorker` bridge.
6. The neutral module-private capability-tool adaptation seam.
7. Gmail read and Salesforce approved mutation through official MCP adapters.
8. One-shot Countdown and Reminder neurons.
9. The complete Azure durability profile.
10. One hosted restart proof across all five module families.

Plan approval freezes its public seams and TDD order. Execution then proceeds one failing public proof
at a time; discovering contradictory compiler, package, or official-service evidence triggers a
recorded correction rather than an invented abstraction.

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
4. Extend `DigitalBrain.Google` from the Foundation `IGmail` root to `ICalendar` when a concrete
   calendar story exists.
5. Add recurring/calendar Time vocabulary after its library and public record shapes are approved.
6. Add `DigitalBrain.Flutter` containing only Flutter neurons and its contract drift guard.
7. Design `DigitalBrain.Memory` independently around its own vocabulary. Do not infer its
   architecture from AI, Tasks, or Time.

No deferred item justifies retaining a rejected abstraction today.
