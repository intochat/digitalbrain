# Architecture

DigitalBrain is a small durable neuron kernel plus independently shipped domain modules. This page is
organized the way the code is: the kernel first, then the rule every module obeys, then one section
per module, then the three cross-cutting rails — behaviors, discovery, and hosting.

## 1. The vision

Neurons are durable Orleans-journaled agents. Synapses are typed messages carrying full lineage.
Simulations fire synapses into a real in-process cluster and assert on the resulting timeline. Those
three primitives are the whole substrate. What makes them worth building is what sits on top:

> A brain you program by writing ordinary C#, and that can program itself.

Five sentences carry the rest of the design:

- **The typed interface is the surface, the synapse is the substrate, the generator is the bridge.**
- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, and it replies. Both are journaled; neither is privileged.
- **Modules own vocabulary.** Synapse records and neuron interfaces are compile-time artifacts; adding
  one needs a rebuild.
- **Behaviors own logic.** Single-file C# scripts are runtime artifacts; installing one needs only
  approval.
- **The client API is the programming model.** The same file runs outside the cluster as a script and
  installs inside it as a behavior.

Every install is a human-approved proposal, journaled and reversible. That is the product, not a
feature of it, which is why the rest of this document spends so much effort keeping domain knowledge
out of the kernel and keeping vocabulary typed.

## 2. The kernel

`DigitalBrain.Kernel.Neuron` owns neuron mechanics and nothing else:

- Receive and dispatch incoming synapses.
- Emit, send, and reply with outgoing synapses.
- Journal and observe traffic in both directions.
- Persist operational neuron state.
- Enforce owner, delivery, and concurrency invariants.

The kernel must never contain model inference, provider names, `IChatClient`, prompts or responses,
OAuth details, UI contracts, or semantic memory. The test for a proposed kernel change is simple: if
the kernel would have to know what an LLM, a mailbox, or a CRM record is, the change belongs in a
module.

### Typed requests are reified as causal facts

A typed interface call is a request, not a synapse — but it still has to be visible in the journal.
The kernel resolves that by committing a fact *about* the call rather than turning the call into one:

1. Before invoking, the caller commits `CapabilityRequested`.
2. Its `SynapseDelivery` travels through the Orleans `RequestContext`.
3. The target commits that same delivery to its incoming journal *before* the method body runs.
4. The target executes with that delivery as its causal context.
5. Synapses emitted during the call inherit the correlation and use the request's `SynapseId` as
   their causation.
6. `CapabilityCompleted`, `CapabilityFailed`, or `CapabilityRejected` records the outcome.

These generic facts carry identity, caller, target, contract, method, correlation, causation,
timestamp, and outcome — and deliberately nothing else. Arguments, prompts, secrets, tokens, return
values, and exception content never enter a kernel journal. A module that needs payload-level audit
emits its own typed fact.

Be clear about what this buys: it records attempted, accepted, completed, failed, rejected, and
visibly incomplete requests. It is not exactly-once RPC. Safe retries remain the responsibility of
domain `CommandId`, revision fencing, provider idempotency, and reconciliation.

### The one deliberate exception

A private off-turn runner has to carry an already-committed request across the Kernel/AI assembly
boundary, and it is not a neuron. `DigitalBrain.Kernel.CapabilityDelegation` is the single public
type that exists for this. It is sealed, opaque, non-constructible by consumers, hidden from
IntelliSense, and non-semantic — never a neuron contract, synapse, registry entry, or behavior
vocabulary. The kernel alone mints, carries, validates, durably redeems, and records outcomes for it.

A delegation binds the committed request, the causal caller, the runner grain the filters physically
observe, the owner, the exact target, the contract and method, correlation and causation, and a
one-use identity. It carries nothing from Tasks, AI, MAF, checkpoints, approvals, integrations, or
leases — those semantics stay with the modules that own them. Every off-turn participant or
integration call gets its own precommitted request and its own delegation; the initiating
Task-to-worker request authorizes nothing later. A raw non-neuron call, a forged context, a replay,
or a mismatched source, owner, target, or operation is rejected before the target's method body
starts. Consumption is durable before invocation, so a crash may require a fresh request and
delegation — the cross-grain boundary is not exactly once.

## 3. The module model

Each domain ships as its own package family:

```text
DigitalBrain.Modules.<Name>.Contracts
DigitalBrain.Modules.<Name>
DigitalBrain.Modules.<Name>.Aspire.Hosting   optional
```

`.Contracts` references only `DigitalBrain.Abstractions` — never another module, never a provider SDK.
The runtime package owns neurons and vendor adapters. The Aspire hosting package owns resources,
parameters, authentication setup, and projection into the silo. This split is enforced by tests, not
by convention alone.

### Namespaces are the vocabulary

Physical package names carry packaging detail and may say `Modules` and `Contracts`. Public
namespaces carry meaning and never do:

```text
DigitalBrain.AI.ILLM
DigitalBrain.AI.Ollama.ILlama32
DigitalBrain.AI.OpenAI.IGpt56
DigitalBrain.Google.IGmail
DigitalBrain.Salesforce.ISalesforce
DigitalBrain.Tasks.ITask
```

The namespace and type name *are* the identity. There is no descriptor, enum, tier, or lookup table
that resolves to them. This is also the vocabulary a future natural-language layer resolves against,
which is why it is treated as architecture rather than as naming taste.

### Selection is explicit

Infrastructure is declared in AppHost:

```csharp
var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var journal = storage.AddBlobs("journal");

var brain = builder.AddBrain("brain")
    .WithDevelopmentStores();

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain)
    .WithReference(journal);
```

The silo stays boring:

```csharp
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));
```

The blob resource is not decoration. `AddDigitalBrainJournalStorage` throws when no `journal`
connection string is configured, so a silo that references the brain and nothing else does not start.

Package reference means *available*. `AddModule<T>()` means *selected and configured*. Each module is
added exactly once; a repeat call is a composition error, not a merge.

Compilation generates the module catalog and the runtime composition from the referenced module
types. AppHost projects the selected module manifest and its resource configuration; startup fails
when AppHost selects a module the silo's compiled catalog does not contain. Runtime assembly scanning
is not a mechanism this framework has — a catalog that can be discovered at runtime is a catalog that
can drift from the code that was compiled.

## 4. The modules

Each subsection below states what the module owns, what it must never do, and what is settled but not
yet standing up. `Status: Built` means the contracts and runtime described here exist in the
repository and are exercised by its test tiers. `Status: Designed` means the decisions are ratified
and reversing one requires writing down the reversal — but no code exists yet.

### 4.1 AI

Status: Built

AI owns inference and orchestration vocabulary. Two contracts, deliberately separate even though
their wire shape is identical: `ILLM` means model inference, `IAgent` means an agent with instructions
and capabilities. `ILLM` never inherits `IAgent`, and no adapter may pretend a raw model is a durable
agent.

```csharp
namespace DigitalBrain.AI;

[Alias("ai.llm")]
public interface ILLM : INeuron
{
    [Alias("Ask")]
    Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages);
}
```

The public conversation boundary is Microsoft.Extensions.AI `ChatMessage` and `ChatResponse` — not
framework-owned string DTOs, and not Microsoft Agent Framework types, which stay internal to the
runtime package. Callers do not supply `ChatOptions`; the concrete typed model or agent owns its
model, instructions, tools, and inference configuration.

The concrete type is the identity:

```csharp
public sealed class Llama32(
    [Llm<Llama32>] IChatClient chatClient)
    : LLM(chatClient), ILlama32;
```

`IChatClient` is private to concrete `LLM` neurons and keyed by the concrete neuron type. Only those
neurons may receive it; agents consume `ILlama32`, `IGpt56`, or another concrete model contract.
There is no routing tier, balancing layer, provider enum, capability score, or fallback catalog, and
none may be reintroduced — an architecture test asserts that every concrete `LLM` follows the
namespace, contract, and typed-key grammar, and that `IChatClient` injection stays confined to those
neurons.

The exclusion list is longer than routing, and the rest of it is easy to reintroduce by accident
through hosting rather than through code. Named accounts, provider failover, cost balancing, and
per-model credentials are all deliberately out. Hosting supports exactly one connection per provider,
and the API-key parameter belongs to the module rather than to any individual model — so two OpenAI
models share one secret and one provider resource. A second account, a cheaper fallback, or a
model-specific credential is not a configuration knob anyone can turn; it is a design change that has
to be argued, because each of them smuggles back the selection tier this module exists to avoid.

**Microsoft Agent Framework owns execution.** DigitalBrain must not build a second agent loop,
group-chat engine, handoff engine, workflow engine, session format, or tool middleware stack.
Orchestration is selected by typed base class — the application class name says *what* the team is,
the base class says *how* it operates. Orchestrations accept both raw `ILLM` neurons and role-bearing
`IAgent` neurons; internal adapters convert either into an MAF participant. Participants are declared
by typed neuron identity, never by injecting fake constructor dependencies.

**Orleans is the durability authority.** There is exactly one outer MAF artifact per entry path: a
direct `RespondAsync` turn owns a protected serialized `AgentSession`; a supervised Attempt owns a raw
MAF workflow checkpoint lineage instead. The supervised checkpoint may contain MAF-owned participant
sessions internally — DigitalBrain neither extracts them nor keeps a parallel outer session. There is
no second transcript, and the MAF Durable Extension is rejected because it would duplicate Orleans.
Each envelope binds the DigitalBrain state version, the MAF version, the definition fingerprint, and
the typed participants. Restore reconstructs the exact composed definition first and only then
restores state; a change to participants, prompts, providers, tools, orchestration shape, or MAF
version preserves the old state and demands an explicit migration or reset instead of silently
reusing it.

**A worker never runs a long MAF superstep on its Orleans turn.** It persists replayable input and one
active run — a fresh run identity, the full attempt cursor, the definition fingerprint, the input
checkpoint, and a recovery deadline — and returns. A private runner advances exactly one Lockstep
superstep, and the worker adopts the result only when run identity, cursor, fingerprint, and input
checkpoint all match and the checkpoint store has committed. That runner is infrastructure: no
registry entry, no journal, no semantic interface, no scripting visibility.

Settled but not yet standing up: `Sequential`, `Handoff`, and `Magentic` are ratified orchestration
vocabulary and have no base class in the repository — `GroupChat` and `Concurrent` do. A single-agent
hard task is expected to use a one-participant `Sequential` worker once that type exists.
Compaction is ratified with the shape it will have to keep — internal, token-budget driven,
collapsing old tool results first, summarizing with the same typed model, truncating only as an
emergency, and never leaking experimental MAF types into public contracts — and none of it is
written. Nothing in the repository compacts a conversation, summarizes one, or reasons about a token
budget. The kernel's `NeuronFeed` trims a journal feed by entry count and byte size, which is a
different mechanism answering a different question and must not be read as this one.

### 4.2 Tasks

Status: Built

A Task is durable domain identity for a desired outcome. An MAF Workflow is how one Attempt at that
outcome executes. The Task survives worker, model, orchestration, and deployment changes; the Attempt
does not.

The dependency direction is one-way and load-bearing: `DigitalBrain.Modules.AI.Contracts` references
`Tasks.Contracts`, never the reverse. Tasks knows nothing about AI, MAF, models, prompts, executors,
sessions, or checkpoints, and a test asserts that its contracts assembly cannot even reach them.

Tasks owns only extension vocabulary — abstract `Goal`, `Result`, and `Failure`, a `FactReference`
pairing a source neuron with a fact, and a `TaskPolicy` of maximum attempts, retry delay, and optional
deadline. Applications and modules define the concrete types. There is no `object`, no arbitrary
JSON, no metadata dictionary, no generic event string, and no prompt anywhere in this module.

That leaves an obvious question: if Tasks may not know what a prompt is, how does a `Goal` ever reach
a model? Through two protected abstract methods on the AI `GroupChat` base class. One turns the
immutable `Goal` into the chat messages a workflow starts from; the other turns the workflow's
terminal messages back into a typed `Result`. Both are deterministic and synchronous, and the base
class copies messages in each direction so that neither MAF nor the application ends up holding a
reference into the other's state. That is the entire bridge. Tasks never learns what a `ChatMessage`
is, AI never learns what any particular `Goal` means, and the application class that already defines
both vocabularies is the one place the translation lives.

Four other shapes for that seam were considered and rejected: a generic `GroupChat<TGoal, TResult>`,
a public mapper interface, a reflection convention over method names, and a service-locator lookup.
Each one moves the mapping out of the single class that provably knows both sides and into somewhere
it can be mis-wired at runtime instead of failing to compile.

Retry timing is where the module's independence was nearly lost. A retryable failure waits a fixed
`RetryDelay` before another Attempt, and it waits on private durable reminders owned by the Task
neuron rather than on a Time schedule. The reason is deployment, not taste: a Task that booked its
retries through the Time module would force every application that wants Tasks to also deploy Time.
The contracts test that pins the Tasks dependency list names `DigitalBrain.Time` alongside AI, MAF,
and the integrations as assemblies it must not be able to reach.

The lifecycle is deliberately small — `Pending`, `Running` and `Waiting` moving in both directions,
`Cancelling`, and the immutable terminals `Succeeded`, `Failed`, and `Cancelled`. `Waiting` carries a
typed blocker (`InputRequired`, `ApprovalRequired`, `DependencyPending`, `RetryScheduled`,
`OutcomeUncertain`) so the Task knows blocker identity, category, revision, and resolution while the
worker keeps the detail.

Four rules make concurrency tractable:

- **Exactly one Attempt is active per Task.** Parallel thinking belongs inside that Attempt. Two
  deliberately competing solutions are child Tasks under a parent, not attempts racing on one Task.
- **Revision fencing is strict, and the fact path and the cursor path enforce it differently.** A
  worker's attempt fact is accepted only when task, worker, attempt, and revision all match, and
  every other fact — older or newer — is durably ignored: `Matches` compares
  `fact.Revision == data.Revision`, and a caller that gets `false` returns without touching state, so
  a future-revision fact produces neither a retry storm nor a corruption signal. The worker's own
  cursor path does reject: `GroupChat` requires an incoming cursor to be exactly its next revision
  and throws on anything else. Either way a terminal Attempt refuses continuation and a retry always
  gets a new attempt identity.
- **An Attempt failure is not a Task failure.** Policy may start another sequential Attempt, enter
  `Waiting`, or declare terminal failure. A later retry is a successor Task linked by `RetryOf`.
- **Cancellation is truthful.** It is best-effort intent, never pretend rollback. A cancelling worker
  may honestly report cancellation, a success that won the race, a failure, or an uncertain outcome.
  A completed external effect is never described as cancelled — compensation is an explicit
  capability or a successor Task.

`IWorker` requests are short and idempotent: validate, persist, schedule an internal turn, return.
Only session-owning orchestration neurons implement `IWorker`; ordinary stateless agents and raw LLMs
do not. Workers report typed attempt facts, and the Task accepts a fact only when task, worker,
attempt, revision, and caller all match.

One mapping is tempting enough to get wrong that it is settled explicitly: MAF's run status is not a
Task state, and no adapter may treat it as one. A running workflow means the Attempt is executing. An
idle checkpoint means a superstep ended, not that the Attempt finished — adopting it as completion
would declare success for work that has not happened. Workflow output may complete an Attempt.

Settled but not yet standing up: a pending MAF request mapping to Task `Waiting` with a typed blocker,
and a workflow error feeding Task retry policy rather than terminating the Task on its own. Neither
exists in the repository — `WorkflowRunner` watches only `WorkflowOutputEvent` and
`SuperStepCompletedEvent`, and `AttemptWaiting`/`AttemptFailed` are constructed nowhere under
`modules/` or `src/`, only inside a hand-rolled fake worker that exercises `TaskNeuron`'s state machine
in isolation. What happens today when a run fails is infrastructure recovering itself rather than Task
policy hearing about it: `GroupChat`'s own `db.ai.workflow-run` reminder notices the active run past
its recovery deadline, mints a fresh `RunId` on the same cursor, and retries the superstep — the Task
is never consulted.

### 4.3 Google

Status: Built

`IGmail` is a semantic capability, not an MCP toolset. It means "Gmail behavior" — it does not mirror
whatever a `tools/list` response happens to contain today. The official MCP client, OAuth, token
refresh, transport schemas, session lifetime, schema admission, and invocation all stay inside the
module.
Raw MCP clients, tool names, protocol DTOs, and tool dictionaries never cross the module interface,
and an MCP tool name never becomes permanent public domain vocabulary just because a server exposes
it.

The public surface is therefore small on purpose. A high-level typed method is added only when a real
deterministic non-agent caller needs one, and today the whole of `IGmail` is one message read that
returns an id, a subject, a sender, and a plaintext body.

Behind that method the MCP boundary is private in the literal sense rather than the aspirational one:
the two interfaces it is assembled from — one that produces the OAuth options, one that reads a tool
and calls it — are `internal`, so no contract package, behavior, or caller outside the module can name
them, let alone reach the MCP client they wrap. The neuron is the only door.

Every operation opens a fresh authenticated MCP session, lists what the server advertises, and refuses
to continue unless the exact tool it came for is there. Admission is a positive check rather than a
filter over whatever arrived: the tool must be annotated read-only, must not be annotated destructive,
and its input schema must require the exact typed property the module intends to send. A tool that
fails any of those throws instead of degrading into a best-effort call, because a Gmail server that
has quietly changed what a name means is not a situation to muddle through.

Schema drift between reading a tool and calling it is caught by fingerprint. The module hashes a
canonicalized form of the advertised input schema — object properties written in ordinal order, so
reformatting alone cannot change the hash — at admission, then re-reads and re-compares that
fingerprint immediately before it invokes. A server that reshapes the tool between those two moments
fails the call rather than sending arguments built for the old shape into a new contract.

Authorization is the module's own problem too. Client id, secret, and redirect URI come from module
configuration, the requested scope is the read-only Gmail scope, and the authorization code is
collected by a local loopback listener. Tokens live in the neuron's durable state, encrypted under a
data-protection purpose named for this module and committed through the neuron's own state write — so
a refresh survives deactivation without introducing a second store to keep in sync.

Google does not depend on AI. An application agent composes `IGmail` with a concrete LLM neuron;
`IGmail` never composes a model.

Settled but not yet standing up. None of the following exists in the repository, and the tool path in
particular should not be read as describing today's code:

- `DigitalBrain.Google.ICalendar` is ratified vocabulary waiting for a concrete calendar story.
- The provider-neutral capability-tool seam through which AI would borrow integration capabilities as
  transient model-facing functions. It is ratified as module-author infrastructure — invisible to
  contract packages, behaviors, and natural-language discovery — handing the model only selected exact
  function schemas and never a raw invoke escape hatch. No such seam, middleware, or context provider
  is implemented.
- Its selection policy: availability bounded by a token budget rather than a fixed tool count, hybrid
  retrieval when the granted catalog does not fit that budget, previously used tools sticky within a
  session, and summaries and embeddings kept as disposable discovery indexes while invocation always
  uses the exact current schema.
- `FindCapabilityTools`, the always-available read-only recovery search over the pinned granted
  catalog. A miss may retrieve only previously unseen tools and rerun with finite progress, and there
  is deliberately no raw string invoke beside it.

When that path is built, every selected tool must still route back through the neuron, so that
authorization, incoming request journals, and approval validation keep exactly one home.

### 4.4 Salesforce

Status: Built

`ISalesforce` follows every integration rule in §4.3 — semantic capability, private MCP catalog,
module-owned OAuth and Aspire resources, no AI dependency. What it adds is the mutation story, because
Salesforce is where DigitalBrain writes to a system it does not control.

External mutations use a durable command protocol owned by the integration neuron. Every mutation
carries a `CommandId` and a canonical payload fingerprint and moves through:

```text
Proposed
  -> AwaitingApproval
  -> Approved
  -> Invoking
  -> Completed
             \-> OutcomeUncertain
```

The same command identity and fingerprint resume the work or return the recorded result. Reusing an
identity with different content is rejected. Human approval binds to the exact fingerprint, so an
approved payload cannot be swapped between the moment a person read it and the moment it is sent.

The pause between proposal and approval is not machinery. Proposing a description records the
mutation, leaves it `AwaitingApproval`, and returns a receipt; resuming it is a second ordinary
interface call, `ISalesforce.ApproveAccountDescriptionAsync`, carrying the approval record together
with the durable delivery that proves a human produced it. Nothing intercepts, wraps, or watches the
neuron — which is precisely why the neuron has to do the checking itself, and does. It requires that
the delivery's caller is the approver the approval names, that the synapse inside that delivery is
that same approval record rather than merely one shaped like it, and that the approver is a session
neuron belonging to this neuron's owner. A caller who skips the proposal, mints an approval, reuses
someone else's evidence, or approves a fingerprint that no longer matches the stored payload is
refused before Salesforce is contacted at all. Approving something already finished returns the
recorded receipt instead of writing twice.

Ratified but not built: the operation classification that would let module-declared safe read-only
work be auto-approved while mutating and unknown work still requires a human, and the rule that an
approver agent may advise but never holds authority. The one mutating operation that exists today
always demands human evidence.

Reconciliation is where the design stops being able to bluff. A crash between sending a write and
hearing the answer is ordinary, and the only dishonest response is to assume. So `Invoking` is
committed durably *before* the provider is contacted — the record of "we may already have changed
Salesforce" has to outlive the process that was in the middle of doing it. Recovery then starts by
asking Salesforce what it actually holds: a read-only query for the account, compared field by field
against the payload that was approved. A match is proof and the command becomes `Completed`. A
mismatch, an error, a query that itself fails — none of those prove anything, and each becomes
`OutcomeUncertain` instead of another attempt.

What the module then does is record and return. `ReconcileAsync` persists the uncertain status and
hands back the receipt, and nothing in this module contacts a Task — it could not, because
`DigitalBrain.Modules.Salesforce` references only its own contracts and the kernel, so Tasks
vocabulary is out of reach by construction. The decision belongs to whoever read the receipt, and the
repository's own consumer makes it bluntly: `AccountEnrichmentProcess` throws
`InvalidOperationException` when the state it gets back is anything other than `Completed`.

Ratified but not built: parking the owning Task on an `OutcomeUncertain` blocker rather than letting
the uncertainty surface as a caller-side exception. `AttemptOutcomeUncertain` has no producer under
`modules/`, `src/`, or `samples/`; its only construction anywhere is a scripted fake worker in the
simulations.

The command identity travels as the provider idempotency key wherever a provider offers one;
Salesforce's update tool does not, which is why reconciliation and not the key is what carries this
module. The rule underneath all of it is that a mutation whose outcome cannot be proven is never
repeated, and it is the same reason
DigitalBrain claims no exactly-once external effect anywhere: the provider is the only authority on
its own state, and the most a durable ledger can honestly offer is a correct label for what it does
not know.

The ledger lives in the neuron's durable state and typed journal — not in a new public service.
Read-only operations stay retryable and do not touch it.

### 4.5 Time

Status: Designed

Time separates *public scheduled behavior* from *private kernel scheduling*, and that separation is
the entire point of the module. Kernel timers and reminders maintain outbox delivery, run recovery,
and retry pumps. Those are infrastructure, and by convention their reminder names begin `db.` — the
kernel outbox registers `db.outbox` and the AI worker's recovery reminder registers
`db.ai.workflow-run`. The prefix is a reading aid for whoever inspects a reminder table, not an
enforced reservation: no code validates it, and durable state keys deliberately do not follow it at
all, which is why the AI orchestration keys are `ai.*`. Time neurons, by contrast, are addressable
schedules that behaviors, Tasks, and modules may talk to. A behavior must never see `IGrainTimer`,
`IGrainReminder`, `TickStatus`, or a raw reminder name.

The public vocabulary is `DigitalBrain.Time.ICountdown` for a one-shot duration and
`DigitalBrain.Time.IReminder` for an absolute or recurring schedule. It is `ICountdown` and not
`ITimer` because .NET 10 already defines `System.Threading.ITimer` and this repository enables
implicit usings — an ambiguous name in a single-file script is a real cost.

What is settled, and why each rule is there:

- **Durability is the promise; precision is not.** Both survive deactivation and silo failure, because
  a schedule that dies with an activation was never a schedule. Neither claims real-time accuracy: an
  occurrence is never intentionally early and is eventually observed after its due time. Anything
  needing a hard deadline needs something other than a wake-up.
- **A schedule is a thing you can address, not a callback you registered.** Each logical schedule is
  one neuron identity with one lifecycle, one current revision, and an explicitly named destination.
  "Who configured this?" and "who receives the occurrence?" are different questions with different
  answers, which is what keeps delivery to another owner from becoming an accident — it requires an
  explicit grant that does not exist yet.
- **Scheduling is obtained by addressing a schedule, never by inheriting a hook.** There is no
  inheritance-based reminder handling in this architecture. `ICountdown` and `IReminder` are separate
  neurons and they are the only types that encapsulate the Orleans reminder and timer implementation;
  a module reaches scheduled behavior by talking to one of them, and a module's own private timing
  stays inside that module rather than being obtained by overriding an inherited callback.
  `ReceiveReminder` is not part of the public neuron surface — today's kernel deviates from that rule;
  see the known deviation in §10. The alternative is worse than it looks:
  once a base class exposes a reminder hook, every subclass that wants a wake-up overrides it, each
  one has to know which names its ancestors already claimed and chain to `base` for the rest, and the
  answer to "whose reminder is this?" ends up spread along an inheritance chain instead of living in
  one neuron.
- **Repeating a request has to be safe, because a caller that crashed cannot know whether it was
  heard.** Start applies only from unscheduled; reschedule and cancel only from scheduled and only
  against an expected revision; restart begins a new generation rather than resurrecting an old one.
  Every mutation carries a `CommandId` whose repeat returns the recorded result. Transitions emit
  typed facts, so a schedule's history can be read without opening opaque Orleans state.
- **Orleans rings the bell; it stores nothing.** Persisted Time state is the authority and the Orleans
  adapter is only a wake-up mechanism. A callback carries schedule identity, revision, and occurrence
  identity and nothing else — never a stored action or payload — which is what lets an uncommitted or
  late callback be recognised and dropped instead of firing work the schedule no longer describes.
  The ordering that earns that property is itself settled, because a crash between any two of its
  steps has to leave a readable state: register the revision-fenced wake-up first, then persist the
  schedule, then retire the previous registration; on cancel, persist `Cancelled` before touching a
  registration at all. A wake-up whose schedule was never committed finds no matching revision, a
  wake-up from a registration already superseded finds a newer one, and a wake-up for a cancelled
  schedule finds a terminal state — all three are dropped rather than acted on.
- **Low latency and durable delivery are bought separately, so they race on purpose.** Production
  registers an activation-local Orleans timer for a prompt wake-up *and* a durable Orleans reminder as
  the backstop that outlives the silo. Both may fire for the same occurrence. The Time neuron
  deduplicates by revision and occurrence, and that deduplication is what makes running two mechanisms
  a safety measure rather than a source of doubled work.
- **Elapsed duration and wall-clock recurrence are different problems and get different types.**
  `IntervalSchedule` is a duration anchored to an instant; `CalendarSchedule` is a wall-clock rule in
  an IANA zone. DST is resolved deterministically instead of inherited from a library default: an
  occurrence inside a gap moves to the first valid instant after it, an overlap fires once at the
  earlier instant, and the fact preserves requested local time, resolved instant, offset, and the
  adjustment that was applied.
- **A missed occurrence is news, not a backlog to replay.** An overdue one-shot occurs once after
  recovery. Recurring misses collapse into a single overdue fact carrying first and last missed time,
  count, recovery time, and revision, and the schedule then advances to the next future occurrence. A
  Reminder is a wake-up, not a durable job queue; work that must happen for every occurrence belongs
  in Tasks, which is the module built not to lose things.
- **The registry indexes schedule contracts, never live schedules.** `ICountdown` and `IReminder` are
  what discovery resolves against. A running schedule is neuron state, and indexing every instance
  would turn a compile-time vocabulary into a runtime directory that drifts.
- **One reminder provider, because the kernel already requires one.** The outbox needs a durable
  Orleans reminder provider whether or not this module is selected, so Time reuses it and must not add
  a second store. In-memory reminders stay development and test only.
- **Tests must never wait on a clock.** Schedules are driven through `TimeProvider` plus a
  deterministic driver, so a simulation can advance a week while no wall-clock time passes.

Explicitly still open: the internal calendar recurrence library and the exact recurring and calendar
record shapes. Do not implement those as though they were settled.

### 4.6 Flutter

Status: Designed

`DigitalBrain.Flutter` will contain only Flutter neurons and its contract drift guard, following the
same package triple and hosting pattern as every other module. It is deliberately outside the first
executable proof, and nothing about it is settled beyond that boundary — no contract shapes, no
transport, no hosting resources. It appears here so that "we will bolt a UI onto the kernel later" is
never mistaken for a design.

### 4.7 Memory

Memory is out of scope — not designed, not deferred-with-a-shape. It carries no status line because
there is nothing to report a status about.

This is a deliberate constraint rather than an oversight. When Memory is designed it must be designed
independently, around its own vocabulary; its architecture must not be inferred from AI, Tasks, or
Time because those modules solved different problems. One rule already binds it: a future Memory may
project synapse journals, but it may never reconstruct truth by scraping traces.

## 5. Behaviors and scripting

Modules create vocabulary and need a rebuild. Behaviors create logic and need only approval. A working
C# file creates live behavior by composing existing typed vocabulary — it does not invent new public
neuron contracts at runtime, and it cannot introduce a new Orleans grain type. When a behavior needs a
permanent typed contract, it is promoted into a module.

Each working file contains exactly one public `Behavior` class; namespace plus class name is its
identity, and a replacement is the same identity at a new approved revision. That keeps a single
proposal from smuggling several behaviors past one approval.

When the behavior compiler exists it will be contract-only:

- **Allowed:** the Behavior API, `DigitalBrain.Abstractions`, selected module contracts, approved BCL
  types, and Microsoft.Extensions.AI message types.
- **Forbidden:** `IGrainFactory`, `IChatClient`, provider SDKs, MCP protocol types, `HttpClient`,
  `IServiceProvider`, filesystem and process APIs, and reflection.

Behaviors are activated externally by existing typed synapses — never dispatched by name — may make
existing typed method requests internally, and emit existing typed synapses. A behavior may supply a
dynamic prompt or persona and may compose a behavior-scoped agent from existing contracts, but it may
not introduce dynamic capabilities, bypass the typed registry, or register that temporary agent as a
public neuron. Dynamic prompts are allowed; dynamic capabilities are not.

Runtime behavior installation is designed and not yet built. The only path to a live behavior is a
human-approved proposal with a journaled, reversible decision, and generated code does not receive a
path around that rail. Until the rail exists, changes arrive the ordinary way — through source
control, review, and a rebuild.

The client API is what makes this coherent rather than a second language: the same file runs outside
the cluster as a script and installs inside it as a behavior.

```csharp
var brain = DigitalBrainClient.Connect(grains, "acme");
await brain.SendAsync<IAnalyst>(
    "incident-42",
    new SummaryRequested("Summarize the incident."));
```

`DigitalBrainClient` is the only public client facade. Owner identity is ambient to it.
`SendAsync<TNeuron>()` enters through the owner-bound session and derives the target neuron type from
the interface; `EmitAsync()` broadcasts a fact through the same deliberate entry point. The client
never returns raw neuron proxies. Authentication remains an edge responsibility — an Orleans client is
a trusted cluster peer.

Inside the brain, one neuron calls another typed capability directly:

```csharp
public sealed class Analyst(ILlama32 llama) : Neuron, IAnalyst, IHandle<SummaryRequested>
{
    public Task HandleAsync(SummaryRequested request, CancellationToken cancellationToken)
        => llama.RespondAsync([new ChatMessage(ChatRole.User, request.Prompt)]);
}
```

## 6. Registry and discovery

The generated catalog is the canonical registry. Its entries derive from the public namespace and
contract type name, the XML documentation, method names and parameter types, the handled and emitted
synapse types, and the owning module. Nothing else is authoritative, and nothing is registered at
runtime.

Natural-language programming is intended to follow one path:

```text
"Read the Gmail message that just arrived"
                      ↓
derived vector search over the generated catalog
                      ↓
DigitalBrain.Google.IGmail
                      ↓
exact typed neuron proxy
```

The rule that keeps this safe: a vector index may *rank* candidates and may never execute an invented
type or bypass exact catalog resolution. The index is derived and disposable; the catalog is the
source of truth. Losing the index costs discovery quality, never correctness.

What exists today is the compile-time module catalog and the generated dispatch composition described
in §3. The canonical neuron catalog assembled from public contracts and synapse vocabulary, and the
semantic index derived from it, do not exist yet — the vocabulary rules in §3 are what make them
writable later without another redesign.

## 7. Hosting and durability

AppHost declares infrastructure explicitly and the silo composes it (see §3). Two storage profiles are
ratified — one development, one durable — and what AppHost runs today is neither of them cleanly. It
calls `WithDevelopmentStores()` for clustering, grain storage, and reminders, and then declares an
Azurite-backed blob `journal` resource beside it that belongs to no profile and is wired straight into
the silo reference. The composed result is a development brain with one Azure-shaped store bolted on,
which is why §3's excerpt has to show both halves to be runnable at all.

`WithDevelopmentStores()` is explicitly non-durable — development clustering, memory grain storage,
and memory reminders. It is honest about being a development convenience rather than pretending to be
a lightweight production mode.

The first durable profile is a single Azure Storage resource supplying Blob-backed neuron journals and
Table-backed Orleans clustering and reminders. Local development runs it against Azurite; deployment
points it at real Azure Storage. No generic durability-provider abstraction is introduced until a
second *complete* journaling, clustering, and reminder profile actually exists — one profile does not
justify an abstraction over profiles.

Settled but not yet standing up: that single-resource durable entry point on the brain, and the rule
that in-memory reminders are development and test only and production rejects them. Neither exists.
`WithDevelopmentStores()` takes no environment, performs no check, and throws nothing — it calls
`WithDevelopmentClustering()`, `WithMemoryGrainStorage("journal")`, and `WithMemoryReminders()`
unconditionally, and no code in `src/DigitalBrain.Aspire.Hosting/` inspects the environment at all.
Read the consequence plainly, because it is the reason this is written down rather than left implied:
a deployment that leaves `WithDevelopmentStores()` in AppHost gets memory-backed reminders for the
kernel outbox pump and for the AI run-recovery reminder, and loses both on the first restart, with no
framework refusal anywhere on that path. The journal storage wiring the silo already calls is the one
piece of this section that exists.

### Observability

Synapse journals are the durable causal truth. OpenTelemetry is a diagnostic projection and never the
audit source — traces sample, expire, and get dropped, and an audit trail that does any of those
things is not an audit trail.

Telemetry forms one correlated chain:

```text
Kernel synapse span
  -> MAF workflow and agent spans
     -> model-client and capability spans
```

Spans carry the identity attributes that let a trace be joined back to the journal — receiver,
synapse, and correlation today, with owner, neuron, synapse type, and causation as the ratified target
set. Sensitive content is off by default, and turning it on is a deliberate act. Aspire receives the
OTLP output.

## 8. Known limitations

These are limits of what stands today, and each one is a boundary someone chose rather than a defect
waiting to be found. They are stated here because a system that hides them is harder to build on than
one that does not.

**An Orleans client is a trusted cluster peer.** `DigitalBrainClient.Connect` takes the owner as a
string and binds every call to it, which stops one owner's neurons being addressed through another
owner's session. That is a correctness boundary, not an authentication claim — a process that can
reach the cluster can name any owner. Authenticate at the edge, and do not publish Orleans clustering
endpoints.

**Journal history is bounded.** A neuron feed retains a recent window — 512 entries, or 512 KB,
whichever binds first — and compacts behind it. A read from a cursor older than that window is
answered with a snapshot and a reset instead of the deliveries that no longer exist. The snapshot
carries per-type tallies and sequence bounds, so totals stay honest even once the entries are gone,
but this is a retained window and a summary rather than an eternal audit log.
Effectively-once processing is also windowed: a neuron remembers the last 4096 handled delivery
identities, and a duplicate older than that window is no longer recognised as one.

**Delivery ordering is local.** Directed delivery is FIFO per target and at least once. A receiver
that refuses a delivery blocks only the later deliveries aimed at that same receiver — the drain
marks that one target blocked and carries on with every other. There is no cross-target ordering, and
none is promised.

**Broadcast addressing.** Broadcast resolves the handler **types** registered for a synapse and
addresses one correlation-derived instance of each, so a fan-out reaches a fresh instance per
correlation rather than a standing subscriber. Those instances are where the traffic actually landed,
which is what a future identity-wide feed will have to account for.

**Client observation is not the final timeline stream.** Journal reads take an `afterSequence` cursor
and can be resumed or watched, which is enough for a simulation to assert on what happened. The client
facade itself has no observation surface — it sends and it emits. A durable per-owner timeline and a
reconnect lifecycle are not built.

**`AsClient()` needs a production credential audit.** A client projection must never inherit
silo-only storage or module secrets. Two of the ways that could go wrong are guarded today: AI
resource configuration, and Google and Salesforce OAuth configuration, are both asserted to reach
`WithReference(brain)` and never `brain.AsClient()`. What is unaudited is the general case, because
the durable storage profile in §7 is not written yet and nothing proves that a profile carrying real
credentials keeps them on the silo side.

**DevUI is not part of the current architecture.** No interactive agent UI is wired, and
`Microsoft.Agents.AI.DevUI` is referenced nowhere in the repository.

## 9. Ratified rules

This is the compact form of every decision above, kept as a checklist so that a change can be tested
against it quickly. It states the ratified target, not a report of what is built — several rules
describe things §4 already marks as settled and not yet standing up. Within that framing the rule is
what wins: **if code contradicts a rule, the code is wrong unless the decision is reversed in
writing.** Where code and rule are known to disagree today, §10 names the deviation rather than
letting the rule quietly soften.

### Kernel and modules

1. Kernel = neuron mechanics only; no AI/provider/memory/UI domain knowledge. Its one opaque
   `CapabilityDelegation` transport seam is infrastructure, never semantic vocabulary.
2. Modules own vocabulary; behaviors own logic over existing vocabulary.
3. AppHost selects modules once; silo is `AddDigitalBrain()` only.
4. Namespaces and type names are the programming vocabulary.
5. Generated catalogs; no runtime assembly scanning as truth.

### AI and MAF

6. MAF owns agent/orchestration execution; DigitalBrain owns durable typed boundaries.
7. One outer MAF artifact per entry path: direct session or supervised checkpoint lineage.
8. Public contracts use MEAI `ChatMessage`/`ChatResponse`; MAF types stay internal.
9. Orchestration-by-base-type (`GroupChat`/`Sequential`/`Concurrent`/`Handoff`/`Magentic`).
10. Orchestrations accept both `ILLM` and `IAgent` participants.
11. Participants resolve by typed `NeuronId`, not fake DI.
12. Individual agents are stateless unless contract says otherwise; workers own sessions.
13. Adopt MAF selectively; reject Harness-as-core and Durable Extension.
14. Orleans persists direct sessions and supervised standard checkpoints in separate envelopes.
15. Compaction internal, token-budget, same typed model.
16. Journals = durable truth; OTel = diagnostics.
17. Fingerprinted session restore; explicit migration/reset.
18. Supervised `WorkflowRun`: **one Lockstep superstep**, then durable checkpoint adoption.

### Behaviors

19. Scripts compose existing vocabulary; never create neuron types at runtime.
20. One Behavior class per file; identity = namespace + class.
21. Contract-only compilation; auto-derived capability manifest.
22. Synapse activation externally; typed requests allowed internally.
23. Dynamic prompts/personas OK; dynamic capabilities forbidden.

### Integrations and MCP

24. Public interfaces = semantic capabilities (`IGmail`, `ISalesforce`), not toolsets.
25. MCP stays module-private behind pinned catalogs.
26. MAF approval middleware; human authority; agent may only recommend.
27. Progressive tool disclosure by token budget + hybrid retrieval; no hard tool count.
28. `FindCapabilityTools` recovery; no raw string invoke escape hatch.
29. Capability roots may expose no MCP-shaped methods; exact tools remain private/transient.
30. No exactly-once claim; durable dedupe + uncertainty handling for mutations.

### Tasks

31. Independent `DigitalBrain.Tasks` module now.
32. Task = durable desired outcome; MAF Workflow = one Attempt's execution.
33. Tasks knows nothing about AI/MAF.
34. `IWorker` short requests + attempt facts; only session-owning orchestrations implement it.
35. One active Attempt per Task.
36. Attempt failure ≠ Task failure; terminal Tasks immutable; retries are successors.
37. Small lifecycle + typed blockers.
38. Cooperative truthful cancellation.
39. Fenced async runner; no long MAF work on grain turn.
40. Typed Goal/Result/Failure + fact references; no untyped payload bag.

### Time and hosting

41. Semantic Time ≠ Kernel private timing.
42. Public names: `ICountdown`, `IReminder` (not `ITimer`).
43. One schedule per neuron; explicit destination; revision + CommandId.
44. Interval vs Calendar schedules; deterministic DST; coalesce overdue recurrence.
45. Persisted Time state authoritative; shared Kernel reminder store.
46. First durable profile: single Azure Storage (`WithAzureStorage`).
47. Deterministic test time via `TimeProvider` + simulation driver.

## 10. Still open, known deviations, and rejected

### Still open

Nothing below is settled architecture. Do not implement one of these as though a decision has been
taken, and do not infer a shape for it from a neighbouring module.

- **The internal calendar recurrence library.** Ical.Net paired with Noda Time is the candidate that
  was raised and never argued to a conclusion. §4.5 settles the behavior a recurrence engine has to
  produce — deterministic DST resolution, coalesced overdue occurrences — and deliberately leaves open
  what produces it.
- **The recurring and calendar Time record shapes.** Only those. The one-shot shapes are frozen —
  `StartCountdown`, `ScheduleReminder`, `CountdownSnapshot`, and `ReminderSnapshot` — and so are the
  names `ICountdown` and `IReminder`. This matches §4.5, which leaves open the recurring and calendar
  shapes and nothing else.
- **Memory architecture.** Out of scope entirely, for the reasons in §4.7.
- **The exact CLR records for the capability-tool seam.** §4.3 ratifies that seam's architecture and
  its exclusions; the records and interfaces that would express it are unwritten.

### Known deviations

One ratified rule and the code disagree today, and it is the rule that stands. §4.5 settles that no
module obtains scheduling by inheriting or overriding a reminder hook, and that `ReceiveReminder`
therefore does not belong on the public neuron surface. The kernel exposes it publicly all the same —
`Neuron` in `src/DigitalBrain.Kernel/Neuron.cs` implements `IRemindable` with a public
`ReceiveReminder` that drains the outbox — and both `TaskNeuron`
(`modules/DigitalBrain.Modules.Tasks/TaskNeuron.cs`) and `GroupChat`
(`modules/DigitalBrain.Modules.AI/GroupChat.cs`) implement `IRemindable.ReceiveReminder`, claim the
reminder names each of them owns, and chain to `base` for every other name. That is a deviation to
close, not a decision to reverse. Closing it belongs with the Time module work, because it is that work
that builds the schedule neurons the inherited hook is currently standing in for.

### Rejected

Each of these was argued and turned down. Reintroducing one is a design change with a case to make,
not a configuration choice.

- **AI logic in the kernel.** Model inference, provider names, prompts, OAuth, UI contracts, and
  semantic memory all belong to modules.
- **Provider routing tiers and balancing.** Any model tier, routing layer, balancing policy,
  capability score, or fallback catalog. This is the exclusion §4.1 exists to protect, and hosting is
  the easiest way to smuggle it back.
- **Public model metadata definitions.** No descriptor, enum, or lookup table that resolves to a
  model; the namespace and type name are the identity.
- **Runtime module scanning.** A catalog discoverable at runtime is a catalog that drifts from the
  code that was compiled.
- **Raw MCP clients crossing module boundaries.** Tool names, protocol DTOs, and tool dictionaries
  stay behind the neuron.
- **A second client facade.** `DigitalBrainClient` is the only public one.
- **Compatibility shims.** No adapter kept alive to preserve a shape that was already deleted.
- **The MAF Durable Extension, and MAF Harness-as-core.** The first duplicates Orleans durability; the
  second would make DigitalBrain a second agent loop.
- **Any raw invoke escape hatch.** A model receives selected exact function schemas or nothing —
  never a string-addressed call beside them.
- **A recurrence library adopted because it is the obvious one.** Ical.Net with Noda Time sits on the
  open list above; treating it as decided, rather than arguing it, is what is rejected here.

## 11. Build order

After the ratified AI, Tasks, behavior, integration, and Time foundations are proven, the remaining
work has a dependency order, and this is it:

1. Complete owner-safe client scripting and the proposal, approval, install, and rollback rail.
2. Generate the canonical neuron catalog from public contracts and method and synapse vocabulary.
3. Add semantic and vector discovery as a disposable index over that catalog.
4. Extend `DigitalBrain.Google` from the `IGmail` root to `ICalendar` once a concrete calendar story
   exists.
5. Add recurring and calendar Time vocabulary once its library and public record shapes are approved.
6. Add `DigitalBrain.Flutter` containing only Flutter neurons and its contract drift guard.
7. Design `DigitalBrain.Memory` independently around its own vocabulary, never inferred from AI,
   Tasks, or Time.

No deferred item justifies retaining a rejected abstraction today.
