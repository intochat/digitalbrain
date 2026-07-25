# Architecture

DigitalBrain is an AI-native operating system built around ready-to-use durable neurons and typed
synapses. Users compose that vocabulary in C# today; the intended product lets them describe
behaviors in natural language and installs the resulting logic only through human approval. This
page separates that destination from what is built in the repository.

## 1. The vision

Neurons are durable Orleans-journaled agents. Synapses are typed messages carrying full lineage.
Method-scoped `TestBrain` instances fire synapses into a real in-process cluster and assert on typed
committed-journal evidence. Those three primitives are the whole substrate. What makes them worth
building is what sits on top:

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

<ArchitectureMap />

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

Most `.Contracts` packages reference only `DigitalBrain.Abstractions` — never a provider SDK.
`DigitalBrain.Modules.AI.Contracts` is the one deliberate module-contract exception: its
AI-to-Tasks.Contracts bridge references `DigitalBrain.Modules.Tasks.Contracts`, and the reverse
reference is forbidden. The runtime package owns neurons and domain behavior. Provider runtimes also
own their endpoint, scopes, exact tool policy, arguments, semantic mapping, and authority decisions.
The Aspire hosting package owns resources, parameters, and projection into the silo. Cross-provider
mechanics are deliberately deeper packages rather than copied module code:
`DigitalBrain.Security` owns purpose-bound durable encryption, and
`DigitalBrain.Integrations.Mcp` owns southbound official-SDK transport, OAuth/token-cache mechanics,
callback-scoped session lifetime, structured-result checks, and canonical fingerprint mechanics.
Provider runtimes depend inward on those mechanics; the shared packages never acquire Gmail or
Salesforce vocabulary or decide which tools are safe. This split is enforced by tests, not by
convention alone.

That southbound package is unrelated to `hosts/DigitalBrain.Mcp`, the northbound MCP server that
exposes selected Neurons through `IDigitalBrain`. The northbound host depends on public client and AI
contracts plus MCP server packages; it does not depend on Gmail, Salesforce, or
`DigitalBrain.Integrations.Mcp`.

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
var brain = builder.AddDigitalBrain("brain");

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

builder.AddProject<Projects.DigitalBrain_Host>("silo")
    .WithReference(brain);
```

`AddDigitalBrain(name)` is the one durable hosting call. It owns a brain-scoped Azure Storage
resource and derives clustering and reminder tables plus Blob-backed journals from it. In Aspire run
mode that same resource runs as Azurite; publishing points the unchanged durable profile at Azure
Storage. There is no separate in-memory hosting profile hidden behind local execution.

The executable remains explicit in `AddProject<Projects.DigitalBrain_Host>` because that compiled
project contains the generated module catalog. The brain resource describes infrastructure and
selected modules; it cannot manufacture an executable or a grain catalog.

The silo stays boring:

```csharp
builder.AddKeyedAzureTableServiceClient("brain-clustering");
builder.AddKeyedAzureTableServiceClient("brain-reminders");
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));
```

The complete silo reference receives clustering, reminders, the `journal` blob, protection material
when a selected module needs it, and the durable-resource health waits. A client reference receives
the clustering connection required for Orleans gateway discovery and nothing else: never reminders,
journals, protection material, or durable-resource waits. The service project loads the Orleans Azure
clustering and reminder providers and registers Aspire's keyed `TableServiceClient`s under the exact
projected resource names. `AddDigitalBrainJournalStorage` still throws when no `journal` connection
string is configured, so an incomplete hand-wired silo does not start.

One brain is one homogeneous Orleans cluster. Executables with different grain/application catalogs
must not reference the same brain and rely on placement luck; each gets its own brain identity and
complete storage profile, even when those profiles share the same underlying Azure Storage account.

Package reference means *available*. `AddModule<T>()` means *selected and configured*. Each module is
added exactly once; a repeat call is a composition error, not a merge.

Compilation turns every referenced module into a typed executable capsule and generates the
executable's catalog from those capsules. AppHost projects the selected module manifest and its
resource configuration; startup fails when AppHost selects a module the silo's compiled catalog does
not contain. Every available capsule prepares serializers needed by its public contracts, while only
selected capsules activate runtime services and broadcast handlers. That split keeps wire types
decodable without silently running an unselected module. Runtime assembly scanning is not a mechanism
this framework has — a catalog that can be discovered at runtime is a catalog that can drift from the
code that was compiled.

## 4. The modules

Each subsection below states what the module owns, what it must never do, and what is settled but not
yet standing up. `Status: Built` means the contracts and runtime described here exist in the
repository and are exercised by its test tiers. `Status: Designed` means the decisions are ratified
and reversing one requires writing down the reversal — but no code exists yet.

### 4.1 AI

Status: Built

AI owns inference and orchestration vocabulary. Two contracts are deliberately separate even though
their wire shape is identical: `ILLM` means model inference and `IAgent` means a role-bearing agent or
orchestration. `ILLM` never inherits `IAgent`, and no adapter may pretend a raw model is a durable
agent. There is no generic `Agent` base that collects instructions or capabilities without giving
them MAF semantics; an agent is a concrete typed neuron contract, and MAF owns its execution path.

```csharp
namespace DigitalBrain.AI;

public partial interface ILLM : INeuron
{
    [Alias(nameof(Respond))]
    Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages);
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

**Orleans is the durability authority for direct turns.** Built today: direct Concurrent/GroupChat `Respond` owns a protected serialized MAF AgentSession (encrypted by `DigitalBrain.Security` via the internal direct session helper). There is no second transcript, and the MAF Durable Extension is rejected because it would duplicate Orleans. Restore reconstructs the composed definition first and only then restores state; a fingerprint mismatch demands explicit migration or reset.

**Supervised Task/`IWorker` orchestration is Designed, not built.** `IGroupChat` still extends
`IWorker`, but `Accept` / `Continue` / `Cancel` throw until a thin Orleans-primary supervised path is
rewritten. The retired private `WorkflowRunner` / `OrleansCheckpointStore` / `AIWorkerState` stack was
deleted as overbuilt reinvention — not as a product vocabulary change. When supervised work returns,
it must re-enter as one Lockstep superstep per runner hop with definition-bound checkpoints, not as a
second agent runtime.

Settled but not yet standing up: `Sequential`, `Handoff`, and `Magentic` base types, plus the supervised
worker path above. `GroupChat` and `Concurrent` exist for direct `Respond`. A single-agent hard task is
expected to use a one-participant `Sequential` worker once that type and supervised wiring exist.
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
a model? Through the ratified supervised bridge on AI orchestration — **designed, not standing today**.
When supervised `GroupChat` returns, two protected abstract methods on that base class own the seam:
one turns the immutable `Goal` into the chat messages a workflow starts from; the other turns the
workflow's terminal messages back into a typed `Result`. Both are deterministic and synchronous, and
the base class copies messages in each direction so that neither MAF nor the application ends up
holding a reference into the other's state. That is the entire bridge. Tasks never learns what a
`ChatMessage` is, AI never learns what any particular `Goal` means, and the application class that
already defines both vocabularies is the one place the translation lives. Today's `GroupChat` is
direct `Respond` only; those mapping methods and the supervised worker path are not in the repository.

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
  cursor path (designed for supervised orchestrations) rejects: an incoming cursor must be exactly
  the next revision and throws on anything else. Either way a terminal Attempt refuses continuation
  and a retry always gets a new attempt identity.
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
exists in the repository. The retired private `WorkflowRunner` / checkpoint stack that once watched
MAF workflow events was deleted with overbuilt supervised reinvention (§4.1). `TaskNeuron` still
handles attempt facts. `DigitalBrain.Tasks.Tests` closes the L1 loop with a test-only `IWorker`
that emits `AttemptAccepted` / `AttemptCancelled` (and a stale `AttemptSucceeded` for revision
fencing). Supervised product workers remain unbuilt: `GroupChat.Accept` / `Continue` / `Cancel`
throw until that thin Orleans-primary path is rewritten; direct `Respond` does not consult Task
policy.

### 4.3 Google

Status: Built

`IGmail` is a semantic capability, not an MCP toolset. It means "Gmail behavior" — it does not mirror
whatever a `tools/list` response happens to contain today. The module owns the official Gmail endpoint,
read-only scope, exact admitted tools, arguments, and semantic result mapping. The shared internal MCP
runtime owns official SDK transport, OAuth/token-cache mechanics, callback lifetime, structured-result
checks, and canonical fingerprint mechanics without exposing any of them as public application
vocabulary.
Raw MCP clients, tool names, protocol DTOs, and tool dictionaries never cross the module interface,
and an MCP tool name never becomes permanent public domain vocabulary just because a server exposes
it.

The public surface is therefore small on purpose. A high-level typed method is added only when a real
deterministic non-agent caller needs one, and today the whole of `IGmail` is one message read that
returns an id, a subject, a sender, and a plaintext body.

Behind that method the MCP boundary is private in the literal sense rather than the aspirational one.
The concrete internal `McpRuntime` opens the official client only inside a bounded callback friend to
provider runtimes and test fixtures. There is no DigitalBrain client interface, factory, returned
session wrapper, or public redirect seam. No contract package, behavior, or application caller can
name the runtime or let the SDK client escape its callback. The neuron is the only semantic door.

Every operation opens a fresh authenticated MCP session, lists what the server advertises, and refuses
to continue unless the exact tool it came for is there. Admission is a positive check rather than a
filter over whatever arrived: the tool must be annotated read-only, must not be annotated destructive,
and its input schema must require the exact typed property the module intends to send. A tool that
fails any of those throws instead of degrading into a best-effort call, because a Gmail server that
has quietly changed what a name means is not a situation to muddle through.

Gmail admits one exact `get_message` tool from the current catalog — name, input, output, and all four
safety annotations — and calls that selected official `McpClientTool` immediately in the same
session. A durable later invocation, such as an approved Salesforce mutation, instead stores a
canonical schema-and-annotation fingerprint, opens a fresh session, re-lists, and compares before the
call. Canonical fingerprinting is shared mechanics; the provider still owns the policy that accepts or
rejects a tool.

The module defines its OAuth client configuration and requested read-only Gmail scope; the shared MCP
runtime performs the protocol and keeps tokens in the neuron's durable state under the shared
purpose-bound protector. Production interactive authorization belongs at an authenticated edge; the
internal runtime fails closed unless the explicit `LocalLoopbackDevelopment` mode selects its private
loopback listener. This keeps a server-side silo from silently turning a developer callback into its
production authentication model.

The `IGmail` Neuron name is the provider-account identity: conceptually,
`IGmail("myemail@gmail.com")`. Each named grain owns its own durable token value, and the protection
purpose includes the complete `NeuronId`, so separate names cannot share authorization/token state.
Callers select the account explicitly; there is no account registry or routing layer.

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

`ISalesforce` follows every integration rule in §4.3 — semantic capability, provider-owned endpoint,
scopes, tool policy and mapping, shared private MCP mechanics, module-owned Aspire selection, and no
AI dependency. What it adds is the mutation story, because Salesforce is where DigitalBrain writes to
a system it does not control.

External mutations use a durable command protocol owned by the integration neuron. Every mutation
carries a `CommandId` and a canonical payload fingerprint and moves through:

```text
AwaitingApproval
  -> Invoking
  -> Completed
             \-> OutcomeUncertain
```

The same command identity and fingerprint resume the work or return the recorded result. Reusing an
identity with different content is rejected. Human approval binds to the exact fingerprint, so an
approved payload cannot be swapped between the moment a person read it and the moment it is sent.

The pause between proposal and approval is not machinery. Proposing a description performs zero MCP
or provider operations, records the mutation once as `AwaitingApproval`, and returns a receipt.
Resuming it is a second ordinary interface call, `ISalesforce.ApproveAccountDescription`,
carrying the approval record together with the durable delivery that proves a human produced it.
Nothing intercepts, wraps, or watches the neuron — which is precisely why the neuron has to do the
checking itself, and does. It requires that the delivery's caller is the approver the approval names,
that the synapse inside that delivery is the same approval record, that terminal replays retain the
originally committed delivery identity, and that the approver is a session neuron belonging to this
neuron's owner. A caller who skips the proposal, mints an approval, reuses someone else's evidence,
or approves a fingerprint that no longer matches the stored payload is
refused before Salesforce is contacted at all. Only after that evidence passes does approval open an
authenticated session and admit the exact read and mutation tools. The approval evidence, admitted
schema fingerprints, and `Invoking` fence are committed in one durable save before the update call.
Approving something already finished returns the recorded receipt instead of writing twice.

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
`DigitalBrain.Modules.Salesforce` has no reference to Tasks contracts or runtime, so Tasks vocabulary
is out of reach by construction. The decision belongs to whoever read the receipt, and the
caller that cannot prove completion must refuse to invent success — treat any non-`Completed`
receipt as failure or uncertainty, never as a silent retry of the mutation. The opt-in sample
`DigitalBrain.AccountEnrichment` is the multi-module behavior example: `IAccountEnrichment` +
`EnrichmentModule` (select with `AddModule<EnrichmentModule>()` on a silo that also selects Google
and Salesforce). Flow: Gmail read → Salesforce propose → human approval → completed enrichment
fact; it refuses any non-`Completed` mutation receipt.

Ratified but not built: parking the owning Task on an `OutcomeUncertain` blocker rather than letting
the uncertainty surface as a caller-side exception. `AttemptOutcomeUncertain` has no producer under
`modules/`, `src/`, or `samples/`; it has no production producer.

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

Status: Built — Countdown only

Time separates *public scheduled behavior* from *private kernel scheduling*, and that separation is
the entire point of the module. Kernel timers and reminders maintain outbox delivery and other private recovery pumps. Those are
infrastructure, and by convention their reminder names begin `db.` — the kernel outbox registers
`db.outbox`. The prefix is a reading aid for whoever inspects a reminder table, not an enforced
reservation: no code validates it, and durable state keys deliberately do not follow it at all, which
is why AI direct-session keys are `ai.*`. Time neurons, by contrast, are addressable schedules that
behaviors, Tasks, and modules may talk to. A behavior must never see `IGrainTimer`, `IGrainReminder`,
`TickStatus`, or a raw reminder name.

The implemented public vocabulary is `DigitalBrain.Time.ICountdown`, a durable one-shot duration.
Its Contracts and runtime packages, deterministic `TimeProvider` test edge, durable Orleans-reminder
wake authority, revision fencing, idempotent commands, cancellation, restart, and committed
`CountdownElapsed` delivery are exercised in `DigitalBrain.Time.Tests`. It is `ICountdown` and not
`ITimer` because .NET 10 already defines `System.Threading.ITimer`.

Everything beyond Countdown remains designed or open and unbuilt: `IReminder`, absolute reminders,
recurring interval and calendar schedules, DST handling, recurrence records, and the recurrence
library. There is no `ScheduleReminder` or `ReminderSnapshot` contract in the repository, and this
document does not freeze either shape.

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
  inheritance-based reminder handling in this architecture. `ICountdown` is the implemented schedule
  neuron; a future `IReminder` will be separate. A module reaches public scheduled behavior by talking
  to a schedule neuron, while its own private timing stays inside that module.
  `ReceiveReminder` is not part of the public neuron surface. Base `Neuron` does not implement
  `IRemindable`; the kernel outbox wakeup is composed, and Tasks, AI, and Time each own private
  reminder names and reject unknown names. The alternative is worse than it looks:
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
- **Durable delivery is one mechanism: the Orleans reminder.** Countdown does not arm activation-local
  `TimeProvider` timers or grain-to-self wake interfaces. The reminder is the sole wake authority;
  early ticks re-arm the remaining due, and late ticks beyond one reminder period mark
  `CountdownResolution.Recovered` while on-time ticks mark `OnTime`. Deduplication is by generation,
  revision, and committed occurrence — not by racing two schedulers.
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
- **The registry indexes schedule contracts, never live schedules.** It indexes the implemented
  `ICountdown` contract and will index a future `IReminder` contract only once that vocabulary exists.
  A running schedule is neuron state, and indexing every instance would turn a compile-time
  vocabulary into a runtime directory that drifts.
- **One reminder provider, because the kernel already requires one.** The outbox needs a durable
  Orleans reminder provider whether or not this module is selected, so Time reuses it and must not add
  a second store. In-memory reminders stay development and test only.
- **Tests must never wait on a clock.** Schedules are driven through `TimeProvider` plus a
  deterministic driver, so a `TestBrain` can advance a week while no wall-clock time passes.

Explicitly still open: the internal calendar recurrence library and the exact reminder, recurring,
calendar, and DST record shapes. Do not implement those as though they were settled.

### 4.6 Flutter

Status: Built (first-vertical vocabulary + L0/L1 journal proofs + C# northbound UI edge); Designed (Dart host, full chrome, product journal observation, multi-principal IdP edge)

The OS surface is not a Flutter app with agents behind it. It is a brain whose **UI vocabulary** is a
Flutter module, and whose **logic** (shell policy, post-auth composition, multi-window orchestration,
settings flows) is behaviors — or, until the Behavior rail exists, ordinary C# compositions with the
same allowlist — composing that vocabulary the way AccountEnrichment composes Gmail and Salesforce.

#### Package family and public identity

Physical packages follow the same triple as every other module:

```text
DigitalBrain.Modules.Flutter.Contracts
DigitalBrain.Modules.Flutter
DigitalBrain.Modules.Flutter.Aspire.Hosting   optional; omit until module-specific AppHost resources exist
```

Public namespaces carry meaning and never say `Modules` or `Contracts`. The domain identity is
**`DigitalBrain.Flutter`**, matching Time (`DigitalBrain.Time`) and Google (`DigitalBrain.Google`). A
host-neutral `DigitalBrain.UI` rename is rejected until a second non-Flutter host is a real consumer
and this section is reversed in writing.

Flutter is the **host runtime family** (pixels, widgets, platform channels) — the same class of
concern as MCP behind Google or MAF behind AI — not a license for a public god type.

#### Semantic neurons — not `IFlutter`

Public vocabulary is small semantic capabilities. Namespace plus type name are the identity. There is
no `IFlutter` mega-neuron, no central UI root, and no second “DigitalBrain desktop” grain.

First vertical public surface (≤5 types; freeze signatures only with red→green proofs):

| Type | Kind | Role |
| --- | --- | --- |
| `DigitalBrain.Flutter.IShell` | Neuron | Addressable blank chrome / host surface for one owner-bound shell |
| `DigitalBrain.Flutter.IScene` | Neuron | Addressable content surface (scene key = neuron key) |
| `DigitalBrain.Flutter.OpenScene` | Request command | Typed method payload to open/present a scene (not a free-form route bag) |
| `DigitalBrain.Flutter.SceneOpened` | Synapse (fact) | Broadcast when a scene is open for projection |
| `DigitalBrain.Flutter.ControlActivated` | Synapse (fact) | Domain user action (control id + intent) — never Flutter widget types |

Out of the first vertical: `IWindow`, login/session policy neurons, navigation stacks, theming,
multi-window layout. Those are later vocabulary or composition over `IShell` / `IScene`.

#### Projection model

The same two primitives as the rest of the brain:

- **Synapse** = fact (broadcast or directed, no reply). Surface lifecycle and domain-relevant user
  intent use synapses (`EmitAsync` / `SendAsync`).
- **Interface method** = request (directed, replies), reified as `CapabilityRequested` → outcome.
  Snapshot/query paths (`Current` / present) use methods when the host needs a typed reply.

Flutter rebuild is a **projection of committed journals and serializable scene descriptors**, never
the ledger. Widget trees, scroll offsets, hover, and frame timing stay host-local. Only
domain-relevant intent and scene lifecycle that other neurons may consume cross the boundary.

C# contracts carry **serializable descriptors only** — primitives, stable ids, closed node kinds,
action identities, revision/causation fencing. No `Widget`, `BuildContext`, Dart types, callbacks, or
Flutter SDK types in any C# assembly. Dart materializes widgets from descriptors.

Reject driving product UI from OTel or traces (journals are durable truth; OTel is diagnostic).
Reject a god widget tree with side-channel HTTP that bypasses `IDigitalBrain` typed contracts
(ProbeHost-class surface).

#### Northbound path

The Flutter/Dart host is a **client of the brain**, not a second kernel and not a silo.

```text
Flutter / Dart host  ──HTTP UI protocol──►  hosts/DigitalBrain.Ui (C# edge)
  no Orleans, no MCP tool dictionaries       auth → OwnerId (dev: config owner)
                                             IDigitalBrain only
                                             AppHost: brain.AsClient()
                                                       │
                                                       ▼
                                             DigitalBrain silo (+ FlutterModule when selected)
```

- **Built:** `hosts/DigitalBrain.Ui` — owner-bound `IDigitalBrain` edge with HTTP
  `POST /shells/{shell}/scenes` and `POST /scenes/{scene}/controls/{id}/activate`. L1 proves those
  map to journaled `SceneOpened` / `ControlActivated` without a Dart process. Production AppHost
  selects `FlutterModule` on the silo and wires the UI host with `AsClient()` only (same trust split
  as MCP).
- **Keep** `hosts/DigitalBrain.Mcp` as agent/IDE northbound — not the product UI path (no tool
  dictionaries on UI contracts; MCP owner binding today is process config, not human IdP).
- **Reject:** Dart embeds Orleans client or silo; Flutter process receives journals, protection keys,
  or reminders; attaching `brain.AsClient()` to a non-.NET Flutter resource as if it were an Orleans
  client.
- **Designed:** Dart host consuming this HTTP surface; production IdP principal→owner bind; product
  journal observation on `IDigitalBrain` for reconnect.
- Edge executable lives under `hosts/` (peer of MCP and the silo host). Do not invent a second public
  client facade beside `DigitalBrainClient`.

Product journal **observation** on `IDigitalBrain` remains unbuilt (§8). First vertical proves
journaled facts at L1 in-cluster and via the UI edge HTTP surface; host watch is a named gap, not
permission to invent a second semantic protocol.

#### Auth edge

Authentication is an **application-edge** responsibility. The client is not an auth boundary; an
Orleans client is a trusted cluster peer. Bind the principal to the owner supplied to
`AddDigitalBrainClient` / `Connect`.

| Concern | Edge | Composition (post-auth / future Behavior) |
| --- | --- | --- |
| Credentials, IdP, cookies, token mint/validate | Owns | Forbidden |
| Principal → `OwnerId` mapping | Owns | Receives ambient owner only |
| Shell/scene UX after bind | May host pixels | Orchestrates via Flutter vocabulary + other modules |
| Passwords / tokens in journals | Never | Never |

Login is **not** a grain auth authority and **not** “a Behavior that authenticates.” Prefer the phrase
**post-auth composition**: edge authenticates and binds owner; composition orchestrates sign-in UX and
downstream wiring. Durable southbound tokens (if any) use `DigitalBrain.Security` purpose-bound
envelopes (MCP pattern), never journal payloads.

Dev: fixed test/config owner (MCP’s `"dev"` pattern only on non-public edges). Production: real IdP at
the edge, then the same `IDigitalBrain` programming model.

#### Contract drift guard

Source of truth: public types in `DigitalBrain.Modules.Flutter.Contracts` (aliases, methods,
properties). Guard: checked-in normalized **golden wire-contract manifest** extracted by reflection
over that assembly; L0 asserts equality. When a Dart wire package exists, the same golden is the
Dart-side oracle. Codegen Dart from Contracts may later accelerate maintenance; the gate remains
golden equality, not “generator exit 0.” No protobuf dual vocabulary; no FFI .NET-in-Dart as the pin.

#### Testing

| Tier | First vertical |
| --- | --- |
| L0 | Package graph: Kernel free of Flutter; Contracts free of Dart/Flutter SDK; capsule + alias + golden pins |
| L1 | Real multi-silo `TestBrain`; real Flutter-module neurons; **scene projected = committed journal fact**; no phone |
| L2 | Only when Flutter or the C# UI edge is a real AppHost resource with readiness |
| L3 | Device/widget/golden — never owner of domain truth; never sole gate |

Headless Dart unit tests may prove pure descriptor→view-model mapping when a Dart package exists;
they do not replace L1 journal proof.

#### Still open (do not implement as settled)

- Scene descriptor node algebra and richer chrome vocabulary beyond the first five types.
- Dart host mapping descriptors to widgets; dual-sided golden equality on the Dart side.
- Product journal observation API on `IDigitalBrain` for host reconnect.
- Whether / when `DigitalBrain.Modules.Flutter.Aspire.Hosting` is needed.
- Multi-principal edge factory beyond singleton `AddDigitalBrainClient(owner)` / process owner config.
- Full desktop chrome, multi-window, notifications, AI pane as product surfaces.

### 4.7 Memory

Memory is out of scope — not designed, not deferred-with-a-shape. It carries no status line because
there is nothing to report a status about.

This is a deliberate constraint rather than an oversight. When Memory is designed it must be designed
independently, around its own vocabulary; its architecture must not be inferred from AI, Tasks, or
Time because those modules solved different problems. One rule already binds it: a future Memory may
project synapse journals, but it may never reconstruct truth by scraping traces.

## 5. Behaviors and scripting

Status: Designed

Behavior proposal, approval, installation, execution, and rollback are unbuilt. No `IBehavior`,
`IBehaviorTest`, behavior runner, or behavior execution framework exists. Those names are not
ratified implementation contracts. The intended rail composes existing typed vocabulary without
inventing public neuron contracts or Orleans grain types at runtime.

The design calls for one public `Behavior` class per proposed file; namespace plus class name is its
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

### OS composition before the rail

Shell policy, post-auth UX orchestration, and multi-module “OS apps” (countdown scene, enrichment
approval surface, AI pane) are **logic over vocabulary**. Until the rail ships they live as ordinary
C# under `samples/` (recommended: `samples/DigitalBrain.Compositions`), one public sealed class per
file, identity = namespace + class name (the future Behavior identity). Bodies use only
`IDigitalBrain` + selected `*.Contracts` + approved BCL — the same denylist as the future compiler.
They are pull-invoked by hosts and tests; they are not installed Behaviors and must not introduce
`IBehavior` product APIs.

Do not confuse this with `samples/DigitalBrain.AccountEnrichment`: that sample is a **compiled
process neuron** (durable multi-module vocabulary). Flows that need new durable process state stay
modules; flows that only compose existing vocabulary stay compositions.

The client API is what makes this coherent rather than a second language: the same file runs outside
the cluster as a script and installs inside it as a behavior. Production apps take `IDigitalBrain`
from DI (`AddDigitalBrainClient(owner)`). `DigitalBrainClient.Connect` remains only for Testing and
host wiring that already hold an `IGrainFactory` — it is not the author story.

```csharp
// IDigitalBrain brain from DI, TestBrain.Client, or Connect wiring
await brain.SendAsync<IAnalyst>(
    "incident-42",
    new SummaryRequested("Summarize the incident."));
```

`IDigitalBrain` is the owner-scoped client contract, and `DigitalBrainClient` is its implementation
and the only public client facade. A brain is hosting state held by `DigitalBrainBuilder`, not a
concrete `DigitalBrain` neuron or an addressable root-neuron interface. Owner identity is ambient to
the client.
`SendAsync<TNeuron>()` enters through the owner-bound session and derives the target neuron type from
the interface; `EmitAsync()` broadcasts a fact through the same deliberate entry point. The client
returns only owner-bound typed capability proxies, never an untyped root. Authentication remains an
edge responsibility — an Orleans client is a trusted cluster peer.

Inside the brain, one neuron calls another typed capability directly:

```csharp
public sealed class Analyst(ILlama32 llama) : Neuron, IAnalyst, IHandle<SummaryRequested>
{
    public Task HandleAsync(SummaryRequested request, CancellationToken cancellationToken)
        => llama.Respond([new ChatMessage(ChatRole.User, request.Prompt)]);
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

AppHost declares infrastructure explicitly and the silo composes it (see §3).
`AddDigitalBrain(name)` creates one complete durable profile: a brain-scoped Azure Storage resource
supplying Blob-backed neuron journals and Table-backed Orleans clustering and reminders. Aspire run
mode uses Azurite for that resource; deployment points the same profile at real Azure Storage. The
three derived resources have brain-scoped names and journal readiness is attached to the silo. An
`AsClient()` reference necessarily receives clustering discovery, but never reminders, journals,
protection material, or durable-resource waits. No generic durability-provider abstraction is
introduced until a second *complete* journaling, clustering, and reminder profile actually exists —
one profile does not justify an abstraction over profiles.

Any selected AI or MCP-backed module also causes AppHost to declare one brain-scoped secret containing
a Base64-encoded 256-bit durable-state key. Run mode generates a cryptographically random key and
persists it for local durability; Publish mode has no default and requires the secret from the
deployment environment. The key is projected only to silos, never clients, and is shared by every
silo in that brain. It encrypts MAF direct sessions and MCP OAuth tokens with distinct purposes today;
supervised workflow checkpoints are a designed purpose on the same package (§8). Provider modules do
not create their own keys or process-local key rings.

The production AppHost also exposes this documentation through Aspire's official JavaScript resource
lifecycle:

```csharp
builder.AddViteApp("website", "../../docs")
    .WithExternalHttpEndpoints();
```

`Aspire.Hosting.JavaScript` owns dependency installation and the VitePress process. The resource is
named `website`, its working directory is `docs`, and Aspire allocates its externally exposed HTTP
endpoint; there is no custom npm installer or fixed port.

Every normal production AppHost build runs the repository `RefreshCodeGraph` target. It initializes
the graph when `.codegraph/codegraph.db` is absent and synchronizes it otherwise, and a command failure
fails the build. Because `aspire start` and `aspire run` perform that AppHost build, the graph served by
the configured project MCP is refreshed through the ordinary application lifecycle rather than a
second checked-in dependency inventory.

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

### Testing

`DigitalBrain.Testing` is the one public packable testing product, and it is development-only. Proofs
run at three tiers; there is no parallel fake runtime:

```text
L0  Compiler/shape     DigitalBrain.Tests contracts, packages, and generators
L1  Kernel semantics   real three-silo DigitalBrainFixture + method-scoped TestBrain
L2  AppHost system     assembly-owned DigitalBrainAppHostFixture<TAppHost> + method-scoped RunningAppHost
```

**L1** is the default depth for module semantics and durability. One `DigitalBrainFixture` owns one
real three-silo cluster and permits one active method-scoped `TestBrain` at a time. Tests therefore
serialize within a fixture, while separate test assemblies may run in parallel. Each `TestBrain`
receives an isolated owner namespace, deterministic clock, closed durability faults, typed
committed-journal evidence, and always-on failure artifacts. `TestOwner` is the isolated owner
identity, and `TestNeuron<T>` is its typed neuron handle.

**L2** is reserved for AppHost composition, real resource `Healthy` state, HTTP endpoints, and
bounded cleanup and failure evidence. Product silo restart is not an L2 resource-command path (see
below). An assembly-owned `DigitalBrainAppHostFixture<TAppHost>` creates one method-scoped
`RunningAppHost`. The package-internal lease is the only AppHost serialization owner; test projects
do not add xUnit collections or global parallelization switches. Each test binds each runtime
resource name once and keeps that handle:

```csharp
await using var host = await fixture.StartAsync(cancellationToken);
var silo = host.Resource("silo");
await silo.WaitUntilHealthyAsync(cancellationToken);
using var client = silo.CreateHttpClient();
```

Cleanup remains graph-owned: it uses Aspire resource commands and terminal observations, and never enumerates or kills processes by name. L1 remains the default for neuron and module semantics. Product silo restart is proven through L1 `TestNeuron.RestartHostAsync` (in-process cluster), not through AppHost resource-restart commands.

Substitutes stop at the closed external edges: scripted `IChatClient` via
`DigitalBrainTestBuilder.ConfigureChatClient` (module smoke), scripted southbound MCP sessions via
internal `ConfigureMcpSessionFactory` / `IMcpClientSessionFactory` (Integrations L1), and the
framework-owned `TimeProvider` already registered on every L1 test. Neurons, journals, filters, and
module logic stay real. `Behavior` remains the name of a user-authored ordinary-test concept; the
testing framework adds no behavior interfaces or behavior fixture hierarchy.
Runtime behavior is not a Neuron (see §5).

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
and can be resumed or watched, which is enough for a test to assert on what happened. The client
facade itself has no observation surface — it sends and it emits. A durable per-owner timeline and a
reconnect lifecycle are not built.

**`AsClient()` remains a security boundary.** A client projection must never inherit silo-only storage
or module secrets. Hosting implements that split (`WithReference(brain)` for silos vs
`brain.AsClient()` for clients). L0 `HostingProjectionContracts` pins journal connection, shared state
key, module ids, AI resource configuration, and Google/Salesforce OAuth as silo-only (never on
`AsClient()`). Every new module projection must extend that proof rather than relying on convention.

**Tasks L1 is closed via a test-only worker, not product supervised orchestration.**
`DigitalBrain.Tasks.Tests` proves Start → Accept → `AttemptAccepted` → Running, idempotent Start
receipts, Cancel → Cancelling → `AttemptCancelled` → Cancelled, and stale-revision fact ignore.
Assembly-boundary tests still keep Tasks free of AI/MAF/Time. Supervised MAF-per-attempt workers
remain designed; no product `IWorker` under `modules/` emits attempt facts yet.

**Google and Salesforce L1 proofs use a scripted southbound MCP edge, not live cloud.**
`DigitalBrain.Integrations.Tests` proves Gmail `ReadMessage` against an admitted `get_message` tool,
refusal when that tool's safety annotations fail the positive admission check, Salesforce propose
without MCP, approval rejection before MCP when human evidence mismatches, and approve → `Completed`
through admitted update/query tools on the same in-process edge. Live OAuth and hosted MCP endpoints
remain out of the default L1 path.

**AI direct Concurrent/GroupChat L1 is closed; supervised remains Designed.** Typed LLM smoke
(`ILlama32`) plus ModuleTests multi-participant Concurrent/GroupChat `Respond` and durable second-turn
session reuse exercise the direct surface. Supervised `IWorker` Accept/Continue/Cancel paths still
throw until a thin Orleans-primary path is rewritten.

**Supervised workflow checkpoints are not built.** `DigitalBrain.Security` is purpose-ready for them;
the supervised Orleans-primary runner and checkpoint adoption path do not ship. Present-tense claims
about encrypting live supervised checkpoints describe design, not running product.

**The OpenTelemetry MAF chain is not built.** Journals remain the durable truth. The correlated
kernel → MAF → model-client span chain is ratified target shape, not an implemented diagnostic
projection in this repository.

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
3. AppHost creates the durable brain and selects modules once; silo is `AddDigitalBrain()` only.
4. Namespaces and type names are the programming vocabulary.
5. Generated typed module capsules and executable catalogs; no runtime assembly scanning as truth.

### AI and MAF

6. MAF owns agent/orchestration execution; DigitalBrain owns durable typed boundaries.
7. One outer MAF artifact per entry path: direct session or supervised checkpoint lineage.
8. Public contracts use MEAI `ChatMessage`/`ChatResponse`; MAF types stay internal.
9. Orchestration-by-base-type (`GroupChat`/`Sequential`/`Concurrent`/`Handoff`/`Magentic`).
10. Orchestrations accept both `ILLM` and `IAgent` participants.
11. Participants resolve by typed `NeuronId`, not fake DI.
12. No generic pseudo-agent layer; concrete typed agents/orchestrations use the one internal MEAI-to-MAF adapter.
13. Adopt MAF selectively; reject Harness-as-core and Durable Extension.
14. Orleans persists direct sessions and MAF JSON checkpoints in separate purpose-encrypted envelopes.
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
25. Shared MCP infrastructure owns official SDK/OAuth/token/session/fingerprint mechanics; providers own endpoint, scopes, exact policy, arguments, authority, and semantic mapping.
26. MCP stays internal behind positive admission; a durable fence is fingerprint-rechecked before its later invocation.
27. Production interactive authorization is an authenticated-edge responsibility; silo loopback callbacks require explicit development mode.
28. Human authority is explicit: proposal performs zero provider operations; exact approval evidence precedes catalog inspection and the durable mutation fence.
29. Progressive tool disclosure remains token-budgeted with no raw string invoke escape hatch; capability roots expose no MCP-shaped methods.
30. No exactly-once claim; durable dedupe, pre-invocation fencing, reconciliation, and uncertainty handling for mutations.

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
42. Public schedule names: built `ICountdown`; designed `IReminder` (not `ITimer`).
43. One schedule per neuron; explicit destination; revision + CommandId.
44. Designed and unbuilt: Interval vs Calendar schedules; deterministic DST; coalesce overdue recurrence.
45. Persisted Time state authoritative; shared Kernel reminder store.
46. `AddDigitalBrain(name)` owns one complete durable Azure Storage profile per brain; run mode uses Azurite.
47. One silo-only brain key protects durable AI and MCP payloads with distinct purposes.
48. Deterministic test time via `TimeProvider` + the `TestBrain` driver.

### Flutter and OS surface

49. Modules own UI vocabulary; behaviors (or pre-rail compositions) own shell/login/window *logic*.
50. Semantic neurons (`IShell`, `IScene`, …); never a public `IFlutter` god type or central UI root.
51. Package family `DigitalBrain.Modules.Flutter*`; public namespace `DigitalBrain.Flutter`.
52. Flutter rebuild projects journals + serializable descriptors; journals remain durable truth.
53. C# contracts carry no Dart/Flutter SDK types; Kernel carries no UI vocabulary.
54. Dart host is a northbound client of a C# `IDigitalBrain` edge — never an embedded silo.
55. Auth at the edge; post-auth composition only; never tokens/passwords in journals.
56. Drift guard: golden wire manifest from Contracts; dual-sided when Dart models exist.
57. L1 proves UI facts on journals without a device; L2 only for real AppHost host resources.

## 10. Still open, known deviations, and rejected

### Still open

Nothing below is settled architecture. Do not implement one of these as though a decision has been
taken, and do not infer a shape for it from a neighbouring module.

- **The internal calendar recurrence library.** Ical.Net paired with Noda Time is the candidate that
  was raised and never argued to a conclusion. §4.5 settles the behavior a recurrence engine has to
  produce — deterministic DST resolution, coalesced overdue occurrences — and deliberately leaves open
  what produces it.
- **Reminder, recurring, calendar, and DST record shapes.** Countdown is built and its one-shot
  records are executable contracts. Reminder vocabulary and record shapes are not implemented or
  frozen.
- **Memory architecture.** Out of scope entirely, for the reasons in §4.7.
- **The exact CLR records for the capability-tool seam.** §4.3 ratifies that seam's architecture and
  its exclusions; the records and interfaces that would express it are unwritten.
- **Flutter method signatures, descriptor algebra, and UI transport under the C# edge.** §4.6 ratifies
  vocabulary shape, projection, northbound split, auth, drift guard, and testing tiers; exact
  `[Alias]` pins and wire transport remain open until first-vertical proofs freeze them.
- **Product journal observation on `IDigitalBrain`.** Required for a live Flutter host reconnect story;
  not required for C#-only L1 journal proofs of the first vertical.

### Known deviations

The inherited reminder deviation is closed. Base `Neuron` has no `IRemindable` surface; the outbox
wakeup is composed, and Tasks, AI, and Time own only their private reminder names and reject unknown
names.

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
- **A public `IFlutter` god neuron, `DigitalBrain.UI` namespace without reversal, Flutter-embedded
  silo, OTel-driven product UI, login grain as IdP, or Behavior product APIs before the install rail.**
  See §4.6 and §5.

## 11. Build order

The remaining designed work has a dependency order. Two tracks are explicit so module vocabulary is
not silently blocked on the self-programming rail, and so the OS product is not claimed before either
track has proofs.

**Self-programming track (product priority for installable logic):**

1. Complete owner-safe client scripting and the proposal, approval, install, and rollback rail.
2. Generate the canonical neuron catalog from public contracts and method and synapse vocabulary.
3. Add semantic and vector discovery as a disposable index over that catalog.

**Module vocabulary track (may interleave whenever a consumer and contracts are settled):**

4. Extend `DigitalBrain.Google` from the `IGmail` root to `ICalendar` once a concrete calendar story
   exists.
5. Add recurring and calendar Time vocabulary once its library and public record shapes are approved.
6. Add the Flutter module family (`DigitalBrain.Modules.Flutter*`, namespace `DigitalBrain.Flutter`)
   with semantic UI neurons, contract drift guard, and L0/L1 proofs per §4.6. C#-only first vertical
   before a Dart host. Ordinary compositions under `samples/` may use that vocabulary; claiming
   installed Behaviors still requires the self-programming track.
7. Design `DigitalBrain.Memory` independently around its own vocabulary, never inferred from AI,
   Tasks, or Time.

No deferred item justifies retaining a rejected abstraction today. Do not invent Behavior execution
APIs to fake the self-programming track. Do not ship a Flutter shell app that bypasses typed
vocabulary and journals.
