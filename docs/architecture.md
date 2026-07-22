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
var brain = builder.AddBrain("brain")
    .WithDevelopmentStores();

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
```

The silo stays boring:

```csharp
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));
```

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
registry entry, no journal, no semantic interface, no scripting visibility. Compaction is likewise
internal — token-budget driven, collapsing old tool results first, summarizing with the same typed
model, truncating only as an emergency, and never leaking experimental MAF types into public
contracts.

Settled but not yet standing up: `Sequential`, `Handoff`, and `Magentic` are ratified orchestration
vocabulary and have no base class in the repository — `GroupChat` and `Concurrent` do. A single-agent
hard task is expected to use a one-participant `Sequential` worker once that type exists.

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

The lifecycle is deliberately small — `Pending`, `Running` and `Waiting` moving in both directions,
`Cancelling`, and the immutable terminals `Succeeded`, `Failed`, and `Cancelled`. `Waiting` carries a
typed blocker (`InputRequired`, `ApprovalRequired`, `DependencyPending`, `RetryScheduled`,
`OutcomeUncertain`) so the Task knows blocker identity, category, revision, and resolution while the
worker keeps the detail.

Four rules make concurrency tractable:

- **Exactly one Attempt is active per Task.** Parallel thinking belongs inside that Attempt. Two
  deliberately competing solutions are child Tasks under a parent, not attempts racing on one Task.
- **Revision fencing is strict.** Older revisions are stale and ignored; a future revision means
  corruption and is rejected; a terminal Attempt refuses continuation; a retry always gets a new
  attempt identity.
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

### 4.3 Google

Status: Built

`IGmail` is a semantic capability, not an MCP toolset. It means "Gmail behavior" — it does not mirror
whatever a `tools/list` response happens to contain today. The official MCP client, OAuth, token
refresh, transport schemas, reconnection, schema filtering, and invocation all stay inside the module.
Raw MCP clients, tool names, protocol DTOs, and tool dictionaries never cross the module interface,
and an MCP tool name never becomes permanent public domain vocabulary just because a server exposes
it.

The public surface is therefore small on purpose. A high-level typed method is added only when a real
deterministic non-agent caller needs one; today that is a single message read. Everything else reaches
Gmail through the tool path below.

When AI uses Gmail as a tool, the module hands the model transient exact function schemas through a
provider-neutral, module-private seam. That seam is module-author infrastructure: contract packages,
behaviors, and natural-language discovery cannot see it, and the model sees only selected exact
schemas — never a raw invoke escape hatch. Tool availability is bounded by a token budget and hybrid
retrieval rather than a fixed tool count, previously used tools stay sticky within a session, and
summaries and embeddings are disposable discovery indexes only: invocation always uses the exact
current schema. Every selected tool still routes back through the neuron, which is why authorization,
incoming request journals, and approval validation have exactly one home.

Google does not depend on AI. An application agent composes `IGmail` with a concrete LLM neuron;
`IGmail` never composes a model.

Settled but not yet standing up: `DigitalBrain.Google.ICalendar` is ratified vocabulary and waits for
a concrete calendar story before it is written.

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
approved payload cannot be swapped after the fact. MAF middleware coordinates the pause and resume,
but the neuron independently revalidates the durable approval — a typed caller cannot route around it.
Safe read-only operations may be auto-approved by module classification; mutating and unknown
operations require a human. An approver agent may advise and never holds authority.

Reconciliation is where honesty matters most. The neuron commits `Invoking` before contacting the
provider and passes the command identity as the provider idempotency key when one is supported. After
a crash in `Invoking`, it reconciles by reading provider state before considering another mutation.
Proven state becomes `Completed`; an unprovable outcome becomes `OutcomeUncertain` and the owning Task
waits. An uncertain mutation is never blindly repeated, and DigitalBrain never claims exactly-once
external effects.

The ledger lives in the neuron's durable state and typed journal — not in a new public service.
Read-only operations stay retryable and do not touch it.

### 4.5 Time

Status: Designed

Time separates *public scheduled behavior* from *private kernel scheduling*, and that separation is
the entire point of the module. Kernel timers and reminders maintain outbox delivery, run recovery,
and retry pumps; those are infrastructure and their reminder names are reserved under `db.*`. Time
neurons are addressable schedules that behaviors, Tasks, and modules may talk to. A behavior must
never see `IGrainTimer`, `IGrainReminder`, `TickStatus`, or a raw reminder name.

The public vocabulary is `DigitalBrain.Time.ICountdown` for a one-shot duration and
`DigitalBrain.Time.IReminder` for an absolute or recurring schedule. It is `ICountdown` and not
`ITimer` because .NET 10 already defines `System.Threading.ITimer` and this repository enables
implicit usings — an ambiguous name in a single-file script is a real cost.

The settled semantics:

- Both survive deactivation and silo failure. Neither promises real-time precision: an occurrence is
  never intentionally early and is eventually observed after its due time.
- Each logical schedule has exactly one neuron identity, lifecycle, revision, and an explicit
  owner-bound destination. "Who configured this?" and "who receives the occurrence?" are separate
  questions; cross-owner delivery needs a future explicit grant.
- Lifecycle requests are revision-fenced and idempotent: start only from unscheduled, reschedule and
  cancel only from scheduled with an expected revision, restart begins a new generation, and every
  mutation carries a `CommandId` whose repeat returns the recorded result. Transitions emit typed
  facts rather than living only in opaque Orleans state.
- Persisted Time state is authoritative and the Orleans adapter is only a wake-up mechanism. A
  callback carries schedule identity, revision, and occurrence identity — never a stored action or
  payload. An uncommitted or late callback is ignored.
- Recurrence splits into `IntervalSchedule` (elapsed duration anchored to an instant) and
  `CalendarSchedule` (wall-clock recurrence in an IANA zone). DST is deterministic: a gap moves the
  occurrence to the first valid instant after it, an overlap fires once at the earlier instant, and
  facts preserve requested local time, resolved instant, offset, and adjustment.
- Missed occurrences coalesce. An overdue one-shot occurs once after recovery; recurring misses
  collapse into a single overdue fact carrying first and last missed time, count, recovery time, and
  revision, then the schedule advances to the next future occurrence. A Reminder is a wake-up, not a
  durable job queue — work that requires every occurrence becomes Tasks.
- The kernel owns one shared durable Orleans reminder provider because the outbox needs it even
  without this module. Time reuses that provider and must not add a second store. In-memory reminders
  are development and test only.
- Tests drive schedules through `TimeProvider` plus a deterministic driver, so simulations advance
  time without waiting on a wall clock.

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
"Ask Google Calendar for tomorrow's events"
                      ↓
derived vector search over the generated catalog
                      ↓
DigitalBrain.Google.ICalendar
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

AppHost declares infrastructure explicitly and the silo composes it (see §3). Storage profiles are
chosen there too, and there are exactly two of them by design.

`WithDevelopmentStores()` is explicitly non-durable — development clustering, memory grain storage,
and memory reminders. It is honest about being a development convenience rather than pretending to be
a lightweight production mode.

The first durable profile is a single Azure Storage resource supplying Blob-backed neuron journals and
Table-backed Orleans clustering and reminders. Local development runs it against Azurite; deployment
points it at real Azure Storage. In-memory reminders are development and test only and production
rejects them. No generic durability-provider abstraction is introduced until a second *complete*
journaling, clustering, and reminder profile actually exists — one profile does not justify an
abstraction over profiles. That single-resource entry point on the brain is settled and not yet
written; the journal storage wiring the silo already calls is the piece that exists.

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
