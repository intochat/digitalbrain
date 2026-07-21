# Approved Architecture Decisions

**Source:** grilling session captured in `conversation.txt`

**Scope:** every decision you approved that forms DigitalBrain architecture

**Companion:** `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` (canonical implementation record)

**Rule:** only entries with an explicit user approval are ratified. Soft interest, “looks better,” or unapproved proposals are marked separately.

Each decision records:

1. **Proposed** — what was recommended
2. **Approved as** — your approval and any constraints you added
3. **Implication** — the architectural rule that follows

---

## How to read this file

| Marker | Meaning |
|---|---|
| **RATIFIED** | You approved; treat as settled architecture |
| **SUPERSEDED** | You approved earlier, then later approved a replacement |
| **NOT APPROVED** | Discussed; do not implement as settled |
| **OPEN** | Explicitly left unresolved |

---

## 0. Session framing (pre-MAF plan choices)

These early menu choices set the next workstream before MAF alignment began. Some details were later superseded by MAF decisions below.

### D0.1 — Next executable plan target

| | |
|---|---|
| **Proposed** | 1) AI vertical slice (`IAgent` → `IGroupChat` on typed LLMs) · 2) Canonical registry · 3) Scripting rail |
| **Recommended** | Option 1 |
| **Approved as** | `› 1` — AI vertical slice |
| **Status** | **RATIFIED** (workstream order; later MAF decisions redefine *how* AI is built) |

### D0.2 — Agent base shape

| | |
|---|---|
| **Proposed** | 1) Abstract `Agent` base for typed agents · 2) Concrete generic `Agent` · 3) Both |
| **Recommended** | Option 1 |
| **Approved as** | `› 1` |
| **Status** | **RATIFIED** — applications define `MailAssistant(ILlama32 llama) : Agent, IMailAssistant` |

### D0.3 — Conversation history ownership

| | |
|---|---|
| **Proposed** | 1) Only in `IGroupChat` · 2) Per agent · 3) Both |
| **Recommended** | Option 1 |
| **Approved as** | `› 1` |
| **Status** | **RATIFIED in principle**, **refined by D1.1** — sole conversational state is MAF `AgentSession` owned by the group/orchestration neuron, not a hand-rolled transcript |

### D0.4 — Group participants

| | |
|---|---|
| **Proposed** | 1) Application-defined typed group · 2) Runtime roster · 3) Hybrid |
| **Recommended** | Option 1 |
| **Approved as** | `› 1` |
| **Status** | **RATIFIED** — concrete typed groups name participants in code |

### D0.5 — First GroupChat orchestration

| | |
|---|---|
| **Proposed** | 1) Deterministic ordered round · 2) Parallel panel · 3) Moderator loop |
| **Recommended** | Option 1 |
| **Approved as** | User redirected: use Microsoft Agent Framework orchestrations instead of a custom first strategy |
| **Status** | **SUPERSEDED by D1.x** — MAF `GroupChat` / `Sequential` / `Concurrent` / `Handoff` / `Magentic` replace hand-rolled orchestration |

---

## 1. Microsoft Agent Framework seam

### D1.1 — MAF owns conversational state; DigitalBrain keeps one outer artifact per entry path

| | |
|---|---|
| **Proposed** | Each `IGroupChat` owns exactly one serialized Microsoft `AgentSession`. Orleans/DigitalBrain persists it. MAF owns conversation/orchestration semantics. DigitalBrain journals synapses for causality. No second transcript, turn counter, or competing checkpoint model. Individual `IAgent` neurons remain stateless participants. |
| **Approved as** | `› apptove` → recorded **Approved** |
| **Status** | **RATIFIED** |

**Evidence-driven clarification (2026-07-21):** The approved rule applies directly to interactive
`RespondAsync`: its sole DigitalBrain-owned conversation artifact is the protected serialized MAF
`AgentSession`. A supervised `IWorker` Attempt instead persists the raw standard MAF workflow
checkpoint lineage as its sole outer MAF artifact. That checkpoint may contain MAF-owned participant
sessions internally; DigitalBrain neither extracts them nor maintains a parallel outer
`AgentSession`. MAF 1.13 exposes no supported public bridge between the outer `AgentSession` used by
the direct path and a workflow checkpoint, so the two entry paths reconstruct the same declared
workflow through separate adapters and never seed one path implicitly from the other.

**Implication:** DigitalBrain does not reimplement group-chat history. Direct chat durably stores the
MAF session; supervised work durably stores the MAF checkpoint lineage; neither path keeps a second
transcript or competing state model.

---

### D1.2 — Agent neuron composes MAF AIAgent

| | |
|---|---|
| **Proposed** | Typed Agent neuron = durable identity + synapse boundary + composed Microsoft `AIAgent`. Microsoft `AIAgent` = execution semantics. Typed `ILLM` = inference only. Do not build a second agent loop. |
| **Approved as** | `› lgtm` |
| **Status** | **RATIFIED** |

---

### D1.3 — Public agent wire uses MEAI, not MAF types

| | |
|---|---|
| **Proposed** | Public `IAgent.RespondAsync(IReadOnlyList<ChatMessage>)` returns `ChatResponse`. `ChatMessage`/`ChatResponse` from Microsoft.Extensions.AI. MAF types (`AIAgent`, `AgentSession`, workflows, events) stay internal to `DigitalBrain.Modules.AI`. No `AskAsync(string)`. No caller-supplied `ChatOptions`. Agents remain stateless; group session supplies context. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D1.4 — Orchestration-by-base-type

| | |
|---|---|
| **Proposed** | Orchestration selected by typed base classes over MAF builders: `GroupChat`, `Sequential`, `Concurrent`, `Handoff`, `Magentic`. Application class name/namespace states *what* the team is; base class states *how* it operates. No `OrchestrationKind`, strategy registry, balancing tier, or hand-built orchestration loop. |
| **Approved as** | `› approve, its exactly what i want` (+ multi-model stress test request) |
| **Status** | **RATIFIED** |

Example shape approved:

```csharp
public sealed class EditorialTeam(
    IWriter writer,
    IReviewer reviewer)
    : GroupChat([writer, reviewer]), IEditorialTeam;

// similarly:
// ResearchPipeline : Sequential(...)
// IndependentReview : Concurrent(...)
// SupportDesk       : Handoff(...)
// PlanningTeam      : Magentic(...)
```

---

### D1.5 — Orchestrations accept ILLM and IAgent participants

| | |
|---|---|
| **Proposed** | `Concurrent` = independent answers to the same question. `GroupChat` = shared deliberation. Both accept raw typed `ILLM` and role-bearing `IAgent`. `ILLM` does not inherit `IAgent`; internal adapters convert either to MAF `AIAgent`. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

Example:

```csharp
public sealed class ModelAnswers(
    ILlama32 llama,
    IGpt56 gpt,
    IGrok45 grok)
    : Concurrent([llama, gpt, grok]), IModelAnswers;
```

---

### D1.6 — Participant resolution by typed NeuronId

| | |
|---|---|
| **Proposed** | Orleans will not inject grain refs into constructors for participants. Declare participants by type: `Participant<T>()` → `NeuronId.For<T>(Id.Owner, Id.Name)`. Explicit overload for shared names: `Participant<ILlama32>("shared-local-model")`. No participant registry, descriptors, DI fiction, or string type selection. |
| **Approved as** | User accepted the correction (`› ok, its much better…`) and redirected grilling to behaviors/scripts rather than rejecting participant identity. Subsequent MAF/Tasks work builds on this rule. |
| **Status** | **RATIFIED** (with later scripting/behavior refinements in §2) |

---

## 2. Behaviors and scripting

### D2.1 — Scripts create behaviors, never new neuron types

| | |
|---|---|
| **Proposed** | A live script cannot introduce a new Orleans grain type. Working file creates a live behavior by composing existing typed vocabulary. Modules create vocabulary (rebuild). Scripts create behavior instances (approval only). Promote to module when permanent typed contract is needed. Semantic discovery resolves NL to exact existing types before generating scripts. |
| **Approved as** | `› yes, approve, Instead, the working file creates a live behavior by composing existing typed vocabulary` |
| **Status** | **RATIFIED** |

---

### D2.2 — One public Behavior class per working file

| | |
|---|---|
| **Proposed** | One file = one behavior class. Namespace + class name identify the behavior (not a grain type). Tooling: `brain run`, `brain propose`, approval installs into fixed `BehaviorNeuron`, replacement = same identity + new approved revision. Prevents multi-entry scripts and hidden multi-behaviors per proposal. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D2.3 — Contract-only behavior compilation + derived capability manifest

| | |
|---|---|
| **Proposed** | Compiler exposes only Behavior API, Abstractions, selected module contracts, approved BCL, MEAI message contracts. Forbidden: `IGrainFactory`, `IChatClient`, `HttpClient`, provider SDKs, `IServiceProvider`, `File`, `Process`, `Assembly`, Reflection. Manifest derived from typed references (handles / uses / emits). |
| **Approved as** | `› contract-only behavior is right way` |
| **Status** | **RATIFIED** |

---

### D2.4 — Synapse-only external activation for behaviors

| | |
|---|---|
| **Proposed** | No `RunBehaviorAsync("name", prompt)`. Installed behaviors activate via existing synapses (`IHandle<T>`), may call typed neuron methods internally, emit existing synapses. Callers never dispatch by behavior name. |
| **Approved as** | `› Its exactly right direction…` (with dynamic-agent clarification request) |
| **Status** | **RATIFIED** |

---

### D2.5 — Dynamic prompts allowed; dynamic capabilities forbidden

| | |
|---|---|
| **Proposed** | Behavior may create scoped MAF agents via `Agent(model: Neuron<ILlama32>(), instructions: …)` with dynamic prompts/personas. Models and tools only from approved typed capabilities. Sessions live in owning behavior/group. Not independently addressable; not in neuron registry. No persistent generic `DynamicAgent` neuron. |
| **Approved as** | `› approve!` |
| **Status** | **RATIFIED** |

---

### D2.6 — Member-level capability grants (initial proposal)

| | |
|---|---|
| **Proposed** | Manifest grants exact members (`IGmail.SearchAsync`, not whole `IGmail`). |
| **User response** | `› Not sure, need more grilling` — preferred MCP under the hood rather than hand-written integration methods |
| **Status** | **SUPERSEDED / refined by §3** — semantic capability neurons + MCP tool catalogs + MAF approval; not ratified as “hand-list every method vs whole interface” alone |

---

## 3. Integrations, MCP, and tool approval

### D3.1 — Generated typed facade over pinned official MCP schema

| | |
|---|---|
| **Proposed** | Do not hand-reimplement Gmail. Official MCP server supplies tools; generate typed facade + dispatcher from pinned `tools/list` snapshot; OAuth/MCP stay inside Google module; AI converts approved surface to MAF tools; calls still pass through integration neuron. MAF tool approval + HITL; approver agent may recommend only. |
| **User response** | Soft accept, then stress-tested with Salesforce (`IGmail as toolset` concern) |
| **Status** | **SUPERSEDED by D3.2 and D3.7** — keep the pinned private adapter, not a generated public MCP-shaped facade |

---

### D3.2 — Integration interfaces are semantic capabilities, not MCP toolsets

| | |
|---|---|
| **Proposed** | `IGmail` / `ISalesforce` = stable semantic capabilities. Do not mirror `tools/list`. Module owns MCP connection, OAuth, tool filtering, schema validation, invocation. Raw MCP types never appear in behavior source or public contracts. Behavior grants capability identity; proposal records exact MCP tool snapshot. Tool changes don’t mutate public interface; new tools unavailable until admitted by module policy. |
| **Approved as** | `› this is much better, approve` |
| **Status** | **RATIFIED** |

---

### D3.3 — MAF middleware enforces tool approval; human is authority

| | |
|---|---|
| **Proposed** | Integration module policy classifies tools. MAF ToolApproval middleware enforces pause/resume. DigitalBrain synapses carry approval request/response. Human is initial authority; optional approver agent recommends only. Read-only may auto-approve when module classifies safe. Mutating/unknown require approval. Schema change invalidates standing approvals. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D3.4 — Capability-only behavior code; progressive disclosure of MCP tools

| | |
|---|---|
| **Proposed** | Behavior code may only reference `Neuron<ISalesforce>()`, never provider toolset strings. AppHost/module policy admits maximum catalog. Proposal pins catalog + schema fingerprint. Runtime progressively discloses tools to MAF. |
| **User response** | `› the direction is right, but…` — reject hard tool-count limits; prefer summarization/vector search |
| **Status** | Direction **approved**, implementation refined in D3.5 |

---

### D3.5 — Adaptive schema-token budgeting via AIContextProvider (no fixed tool count)

| | |
|---|---|
| **Proposed** | Full admitted catalog remains available. MAF `AIContextProvider` injects exact schemas. If catalog fits, inject all; else hybrid retrieval (lexical first, optional vector). Sticky recently used / approved tools. Fill **token budget**, not tool count. Summaries/embeddings are discovery indexes only. Retrieval ≠ authorization. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D3.6 — Catalog-search recovery tool (FindCapabilityTools)

| | |
|---|---|
| **Proposed** | Always-available read-only search over pinned granted catalog. On miss: return matching identities → middleware reloads turn with exact schemas → model calls real tool under normal approval. No generic `InvokeTool(name, json)`. Loop bounded by finite progress (must add previously unloaded tool). |
| **Approved as** | `› approve and check which other useful things MAF exposes…` |
| **Status** | **RATIFIED** |

---

### D3.7 — Semantic capability roots; complete MCP catalog stays private

| | |
|---|---|
| **Proposed** | `IGmail` and `ISalesforce` remain stable semantic neuron identities and need not expose MCP-shaped public CRUD methods. Their complete pinned/authenticated/granted MCP catalogs stay module-private. AI uses a MAF `AIContextProvider` to project transient exact tools. Invocation still passes through the integration neuron for ownership, journals, approval, `CommandId`, and reconciliation. Add a public high-level request only when a real deterministic non-agent caller requires it. |
| **Approved as** | `› approve` after the corrected capability-root recommendation |
| **Status** | **RATIFIED** |

---

## 4. Selective MAF adoption, durability, compaction, observability, sessions

### D4.1 — Adopt / keep-for-later / reject MAF pieces

| | |
|---|---|
| **Proposed adopt** | `AIAgent` + `AgentSession`; built-in orchestrations; workflow-as-agent; standard workflow checkpoints; HITL + tool approval; `AIContextProvider`; compaction strategies (internal); agent middleware; OpenTelemetry (one layer, sensitive content off). |
| **Proposed later** | A2A; Agent Skills as knowledge bundles (never replace C# behaviors); DevUI (dev only); background agents via typed definitions if needed. |
| **Proposed reject** | Full MAF Harness (files, shell, todos, etc.); MAF Durable Extension (duplicates Orleans); file/Cosmos history as authority; declarative YAML/PowerFx workflows as second behavior language. |
| **Approved as** | Folded into durability approval path; user approved subsequent durability/compaction/observability/session decisions that depend on this inventory |
| **Status** | **RATIFIED** as MAF adoption policy |

---

### D4.2 — Durability boundary: Orleans around one MAF artifact; no Durable Extension

| | |
|---|---|
| **Proposed** | Orchestration neuron owns: (1) serialized `AgentSession` for completed conversational state; (2) latest standard MAF workflow checkpoint for unfinished current turn. Orleans persists both. Terminal completion saves session and clears checkpoint. Approval wait saves checkpoint and resumes. Reject MAF Durable Extension. |
| **Approved as** | `› approve` → **Durability is approved** |
| **Status** | **RATIFIED** |

**Evidence-driven clarification (2026-07-21):** The artifacts are path-specific, not a combined
session-plus-checkpoint aggregate. Interactive `RespondAsync` commits its protected outer
`AgentSession`. A supervised Attempt commits its raw workflow checkpoint reference before reporting
progress. The checkpoint store identity is stable for Worker + Task + Attempt and is never derived
from a redispatch `RunId`. Only the checkpoint reported by the completed Lockstep superstep may
advance the worker's committed lineage.

---

### D4.3 — Compaction is internal, token-budget driven, same typed model

| | |
|---|---|
| **Proposed** | No public compaction abstractions. Internal MAF CompactionProvider. Pipeline: collapse verbose tool results → summarize with same typed `ILLM` → truncate oldest atomic groups only as emergency. Tool call/result atomic. Each group participant compacts with its own model. No hidden cheap model / tier router. Compaction state stays in AgentSession. |
| **Approved as** | `› approve` → **Compaction is approved** |
| **Status** | **RATIFIED** |

---

### D4.4 — Journals are durable truth; OpenTelemetry is diagnostics

| | |
|---|---|
| **Proposed** | Journals own causal truth (`SynapseId`, correlation, causation, caller, receiver…). OTel may sample/expire; never Memory/audit source. Kernel spans for synapse delivery; MAF for agent/workflow; MEAI for model. Identity attributes: `db.owner`, `db.neuron`, `db.synapse.id`, `db.synapse.type`, `db.correlation`, `db.causation`. Sensitive content off by default. Memory may project journals, never scrape telemetry. |
| **Approved as** | `› approve` → **Observability is approved** |
| **Status** | **RATIFIED** |

---

### D4.5 — Fingerprinted MAF-state compatibility; explicit migration/reset

| | |
|---|---|
| **Proposed** | No MAF `AIHostAgent` / `AgentSessionStore`. Versioned envelope: DigitalBrain state version, MAF version, definition fingerprint, participants, session, optional checkpoint. Restore only through exact composed definition. Mismatch preserves old state and emits `AgentStateMigrationRequired`. Reset/migration is explicit approved action—never silent discard. Treat as sensitive encrypted state. |
| **Approved as** | `› approve` → **Session compatibility is approved** |
| **Status** | **RATIFIED** |

**Evidence-driven clarification (2026-07-21):** Direct session envelopes and supervised checkpoint
envelopes are separate. Both bind the exact MAF version, definition fingerprint, and typed
participants, but a supervised envelope stores a checkpoint reference and replayable initial input,
not an additional outer `AgentSession`. Definition compatibility is checked before MAF runs.

---

### D4.6 — Durable capability invocation / uncertain mutations

| | |
|---|---|
| **Proposed** | Integration-owned ledger with `CommandId` + canonical fingerprint: Proposed → AwaitingApproval → Approved → Invoking → Completed/OutcomeUncertain. Exact approval binds to the fingerprint. Commit Invoking before MCP, reconcile provider state before retry, never blindly repeat uncertainty, and never claim exactly-once. |
| **Approved as** | Approved in the live continuation after `conversation.txt` while freezing the Foundation PoC |
| **Status** | **RATIFIED** |

---

### D4.7 — Capability requests are causally journaled before invocation

| | |
|---|---|
| **Proposed** | Caller commits `CapabilityRequested` before a typed request. Its `SynapseDelivery` crosses through Orleans `RequestContext`; target commits the same delivery incoming before method execution and runs under it as causal context. Caller records Completed/Failed/Rejected. Generic facts contain identity/outcome only—never arguments, secrets, results, or exception content. This is visibility, not exactly-once RPC. |
| **Approved as** | Approved in the live continuation after the Foundation PoC boundary |
| **Status** | **RATIFIED** |

---

### D4.8 — CapabilityDelegation is opaque public Kernel transport, not vocabulary

| | |
|---|---|
| **Proposed** | Permit one narrowly public `DigitalBrain.Kernel.CapabilityDelegation` transport so a private non-neuron runner can carry an already committed capability request across the Kernel/AI assembly boundary. Kernel exclusively mints, carries, validates, redeems, and records outcomes for it. The token is sealed, opaque, non-constructible by consumers, hidden from IntelliSense, absent from `DigitalBrain.Abstractions` and every contracts package, and non-semantic: it is never a neuron contract, synapse, registry entry, or behavior vocabulary. |
| **Approved as** | `› approve and write down it to md file` |
| **Status** | **RATIFIED** |

The delegation binds only generic causal and transport facts: the committed
`CapabilityRequested` delivery; `CausalCaller`, the GroupChat neuron whose outgoing journal owns
that request; `DelegateSource`, the private runner `GrainId` physically observed by the Kernel
filters; owner; exact target; contract and method; correlation and causation; and an opaque one-use
identity. `CausalCaller` and `DelegateSource` are deliberately different identities. Undelegated
non-neuron capability calls are denied before semantic code executes. Redemption is durably
recorded before invocation, and replay, wrong source, wrong owner, wrong target, wrong operation,
and forged raw `RequestContext` are rejected.

`RunId`, `AttemptId`, `AttemptCursor`, Task revision, definition fingerprint, checkpoint identity,
MAF state, approval state, integration command state, and lease/generation/renewal semantics are
forbidden in the Kernel delegation. AI validates `ActiveRun` before minting and fences returned
results; integration modules retain approval and `CommandId` reconciliation. Every off-turn typed
participant or integration call gets its own exact precommitted request and delegation; the
initiating Task-to-worker request cannot authorize later runner-to-`ILLM`, runner-to-`IAgent`, or
runner-to-integration calls. The cross-grain consume/invoke boundary is not an exactly-once
transaction: recovery may require a newly journaled request and fresh delegation.

This is one concrete infrastructure seam, not a public service hierarchy. Broad friend access, a
public raw-context API, proxy neurons, a global delegation manager, and lease/generation/renewal
frameworks remain forbidden.

**Evidence retained (2026-07-21):**
`AIWorkerContracts.RawRunnerCannotPreserveCausalCapabilityBoundary` first failed 1/1 when the
target's self-check found no committed incoming request. The retained characterization then passed
1/1, proving the current default-allow hole without making the root gate red. The unfiltered root
gate passed `DigitalBrain.Tests` 143/143, `DigitalBrain.Simulations` 94/94, and
`DigitalBrain.HostTests` 5/5: **242/242**, zero failed or skipped. Task 5 must move rejection ahead
of target method entry, then prove the delegation rail.

The executable exclusion boundary for the present repository is that no contracts package
references the token and no generated semantic registry discovers it. Contract-only behavior
compilation does not exist yet; excluding `CapabilityDelegation` from that future compiler remains
an invariant, not current test evidence.

---

## 5. Tasks module and MAF workers

### D5.1 — Task owns durable desired outcome; MAF Workflow owns one attempt’s execution

| | |
|---|---|
| **Proposed** | Old IAW `TaskLedgerGrain` is shallow (list + prompt formatter, no real lifecycle). Correct split: `DigitalBrain.Tasks.ITask` = durable task identity/lifecycle; typed AI worker = attempt execution; MAF Workflow/Session/Checkpoint = how that attempt runs. Task outlives attempts, can switch workers, retry, wait for humans, complete via non-AI work. |
| **User response** | `› looks much better… keep grilling implementation` — accepted direction without rejecting |
| **Status** | **RATIFIED in principle**, details locked by later approvals in this section |

---

### D5.2 — Cooperative Lockstep: one checkpointed superstep per supervised worker turn

| | |
|---|---|
| **Proposed** | Prototype evidence: default OffThread cancel-on-checkpoint **repeats work** (`first=1, second=2, third=3`). Lockstep yields exact one superstep without repeats (`1,1,1`). Supervised hard tasks use Lockstep internally; direct interactive conversations use their separate session-owned adapter. One worker turn ≤ one MAF superstep. Public execution-mode option rejected. |
| **User response** | Asked for deeper ownership grill first; later explicitly approved the one-superstep Lockstep rule |
| **Status** | **RATIFIED** as D5.15 |

---

### D5.3 — Ownership split: Task supervises; task-scoped GroupChat owns MAF; executors private

| | |
|---|---|
| **Proposed** | `TaskNeuron` owns task identity, goal, lifecycle, attempts, worker selection, terminal result—not sessions/checkpoints/executors. The task-scoped orchestration neuron owns the path-appropriate MAF state (direct session or supervised checkpoint lineage) plus workflow reconstruction. MAF executors are private reconstructed runtime objects (no semantic neuron identity, registry entry, or public contract). Each attempt gets distinct worker identity e.g. `{task}/attempt-N`. Reject: TaskNeuron-as-Executor, dual checkpoint storage, public executor IDs, mirroring every MAF event. |
| **Approved as** | `› approve` → **ownership split is approved** |
| **Status** | **RATIFIED** |

---

### D5.4 — Extract independent DigitalBrain.Tasks now

| | |
|---|---|
| **Proposed (assistant)** | Start with `DigitalBrain.AI.ITask` and defer Tasks extraction until a non-AI worker exists. |
| **Approved as** | `› yes, lets extract DigitalBrain.Tasks` — **overrides** deferral |
| **Status** | **RATIFIED** — independent module now |

**Locked package shape:**

```text
DigitalBrain.Tasks.Contracts  → ITask, IWorker, AttemptId, AttemptRequest, AttemptCursor, facts
DigitalBrain.Tasks            → internal TaskNeuron : Neuron, ITask
DigitalBrain.AI.Contracts     → IGroupChat : IAgent, IWorker
DigitalBrain.AI               → GroupChat implements IWorker via MAF

Tasks.Contracts → Abstractions
Tasks runtime   → Tasks.Contracts + Kernel
AI.Contracts    → Tasks.Contracts
AI runtime      → AI.Contracts + MAF

Tasks knows nothing about AI/MAF/models/prompts/executors/checkpoints.
```

---

### D5.5 — IWorker seam: short idempotent requests + attempt facts

| | |
|---|---|
| **Proposed** | `AcceptAsync` / `ContinueAsync` / `CancelAsync` validate, persist, schedule internal turn, return immediately—never run long superstep inline. Outcomes are synapses: `AttemptAdvanced`, `AttemptProgressed`, `AttemptWaiting`, `AttemptSucceeded`, … Facts carry Task, Worker, Attempt, Revision. Only session-owning orchestration neurons implement `IWorker` (not ordinary `IAgent` or raw `ILLM`). Single-agent hard task uses one-participant `Sequential`. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D5.6 — Exactly one active Attempt per Task

| | |
|---|---|
| **Proposed** | Parallel thinking lives inside one attempt (Concurrent/group/branches). Competing solutions = child Tasks under parent, not racing attempts. Retries are sequential with new `AttemptId`. |
| **Approved as** | `› lgtm` |
| **Status** | **RATIFIED** |

---

### D5.7 — Attempt failure ≠ Task failure; terminal Tasks immutable

| | |
|---|---|
| **Proposed** | On attempt failure, Task policy may start another Attempt, enter Waiting, or fail. Terminal `Succeeded` / `Failed` / `Cancelled` are immutable. Later retry = successor Task linked by `RetryOf`. |
| **Approved as** | `› approve` (paired with lifecycle grilling) |
| **Status** | **RATIFIED** |

---

### D5.8 — Small Task lifecycle + typed blockers

| | |
|---|---|
| **Proposed** | `Pending → Running ↔ Waiting → Cancelling → Cancelled`; also terminal Succeeded/Failed from Running/Waiting. Waiting blockers: `InputRequired`, `ApprovalRequired`, `DependencyPending`, `RetryScheduled`, `OutcomeUncertain`. Worker retains MAF/integration detail. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D5.9 — Cancellation is cooperative and truthful (no pretend rollback)

| | |
|---|---|
| **Proposed** | Cancellation is intent. Worker may report Cancelled, Succeeded (race), Failed, or OutcomeUncertain. Completed external effects are never described as rolled back; compensation is separate capability/successor Task. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D5.10 — Recoverable asynchronous WorkflowRun (not a long grain turn)

| | |
|---|---|
| **Proposed** | Worker persists one active `WorkflowRun` (`RunId`, full `AttemptCursor`, definition fingerprint, input checkpoint reference, `RecoverAfter`) plus replayable input and the initiating causal reference, then returns. An internal AI runner (not a public neuron) executes one MAF superstep. Recovery replaces only the `RunId`; checkpoint-store identity remains stable for Worker + Task + Attempt. The worker accepts a result only when the exact active `RunId`, cursor, fingerprint, and input checkpoint match, and only after the returned checkpoint is durable. Cancellation clears `ActiveRun`; late or duplicate results cannot overwrite. Reminders redispatch unfinished runs. Runner has no registry/journal/scripting identity. |
| **Approved as** | `› approve, but also think about proper introduction of ITimer and IReminder…` |
| **Status** | **RATIFIED**, with evidence-driven vocabulary correction to recoverable run (Time workstream remains open from this approval) |

The initiating capability request must be journaled before off-turn execution. Every later off-turn
typed participant or integration call receives its own exact, owner-bound, one-use
`CapabilityDelegation` under D4.8. The token is public only for opaque cross-assembly transport; a
general-purpose public Kernel bypass remains forbidden.

---

### D5.15 — One MAF Lockstep superstep per supervised WorkflowRun

| | |
|---|---|
| **Proposed** | Each supervised `WorkflowRun` advances exactly one Lockstep superstep; restore its input checkpoint → stop at the first `SuperStepCompletedEvent` → durably commit the returned checkpoint before another run may continue. Concurrent may still fan out *inside* that superstep. Interactive non-Task conversations use their separately persisted direct session path. No exactly-once claim is made across the checkpoint-store-commit/worker-adoption crash window. |
| **Approved as** | `› approve, but what about implementation…` |
| **Status** | **RATIFIED** |

**Evidence retained:** MAF Workflows 1.13.0 prototype—OffThread cancel/resume repeated executors; Lockstep did not.

---

### D5.16 — Minimal typed Task extension vocabulary

| | |
|---|---|
| **Proposed** | Tasks owns abstract typed `Goal`, `Result`, `Failure`; `FactReference(NeuronId Source, SynapseId Fact)`; and `TaskPolicy(MaximumAttempts, RetryDelay, Deadline)`. Applications/modules define concrete types. No prompt, `object`, arbitrary JSON, metadata dictionary, or generic event strings. Success stores one typed Result plus evidence references. `OutcomeUncertain` never auto-retries. |
| **Approved as** | Approved during the final Task implementation grilling |
| **Status** | **RATIFIED** |

---

## 6. Time module

### D6.1 — Semantic Time neurons ≠ Kernel private timing

| | |
|---|---|
| **Proposed** | Kernel keeps private outbox/recovery timers. Public `DigitalBrain.Time` neurons are addressable schedules. Behaviors never see `IGrainTimer` / `IGrainReminder` / `TickStatus` / raw reminder names. Kernel names use reserved `db.*`. Internal non-neuron adapter receives raw Orleans callbacks. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.2 — Durable public countdown and reminder semantics

| | |
|---|---|
| **Proposed** | One-shot duration and absolute/recurring schedules both survive deactivation/failure. Not real-time precise—never intentionally early; eventually after due time. Occurrences carry `ScheduledAt`, `OccurredAt`, schedule revision. Reschedule increments revision (stale ignored). Each logical occurrence → one journaled elapsed fact. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.3 — One logical schedule per time-neuron identity

| | |
|---|---|
| **Proposed** | Examples: `task/order-42/deadline`. One NeuronId, owner, revision, journal. No singleton scheduler bag of ScheduleIds. Registry indexes vocabulary contracts, not every temp instance. High-frequency internal retries stay private. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.4 — Explicit owner-bound destination for ticks

| | |
|---|---|
| **Proposed** | Caller ≠ destination. Destination must be same owner (until future cross-owner grant). Time neuron cannot safely infer callee from current capability bridge. One destination per schedule; multi-consumer via destination behavior, not subscription broker on Time. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.5 — Revisioned lifecycle + CommandId idempotency

| | |
|---|---|
| **Proposed** | `Start` only Unscheduled; `Reschedule`/`Cancel` only Scheduled with `ExpectedRevision`; `Read` snapshot; every mutation has `CommandId` (repeat → same result; stale revision → no change; future revision → reject). Restart after Elapsed/Cancelled creates new generation. Emit typed started/rescheduled/cancelled facts. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.6 — Coalesce missed recurring occurrences

| | |
|---|---|
| **Proposed** | Orleans persists definitions, not occurrences. Overdue one-shot fires once after recovery. Recurring: one `ReminderOverdue` fact (first/last missed, count, recovery, revision) then advance to first future occurrence. Destination decides compensate/ignore/Tasks. Reminder = wake-up, not job queue. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.7 — IntervalSchedule vs CalendarSchedule

| | |
|---|---|
| **Proposed** | `IntervalSchedule` = exact elapsed duration. `CalendarSchedule` = wall-clock in IANA zone. Public DigitalBrain value types—no cron strings, no recurrence-library API in contracts. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.8 — Deterministic DST policy

| | |
|---|---|
| **Proposed** | Gap (spring): fire at first valid instant after gap. Overlap (autumn): fire once at earlier instant. Record requested local, resolved instant, offset, adjustment. Never two occurrences for one calendar recurrence. Precision-critical work uses interval/absolute + Tasks. |
| **Approved as** | (approval turn present in session; locked as “DST handling is locked”) |
| **Status** | **RATIFIED** |

---

### D6.9 — Persisted Time state is authority; Orleans adapter is wake-up only

| | |
|---|---|
| **Proposed** | Crash-safe ordering: register revision-fenced wake-up → persist schedule → retire old registration. Cancel: persist Cancelled first. Adapter callbacks carry only neuron id, revision, occurrence id—never arbitrary actions. No distributed transaction claim. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.10 — TimeProvider + deterministic simulation schedule driver

| | |
|---|---|
| **Proposed** | Production: Orleans driver. Testing: controlled `TimeProvider` + deterministic driver; `simulation.AdvanceTimeByAsync(...)`. Never `DateTimeOffset.UtcNow` for schedule math. Driver is not a public neuron contract. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.11 — Shared Kernel reminder provider (not Time-private store)

| | |
|---|---|
| **Proposed** | Kernel owns single durable Orleans reminder provider (outbox needs it without Time). Time reuses it. In-memory reminders = dev/test only; production rejects them. Time module must not add a second store. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.12 — First durable Aspire profile: one Azure Storage resource

| | |
|---|---|
| **Proposed** | `.WithAzureStorage()` → Blob journals, Tables clustering, Tables reminders; Azurite local; real Azure for deploy. `WithDevelopmentStores()` explicit non-durable. No Redis-only or fictional multi-provider abstraction until a complete second profile exists. |
| **Approved as** | `› approve` |
| **Status** | **RATIFIED** |

---

### D6.13 — Public name is ICountdown, not ITimer

| | |
|---|---|
| **Proposed** | `.NET 10` has `System.Threading.ITimer` + implicit usings → ambiguity in scripts. Vocabulary: `DigitalBrain.Time.ICountdown`, `IReminder`, and countdown/reminder facts. |
| **Approved as** | `› do it` |
| **Status** | **RATIFIED** |

---

### D6.14 — Internal recurrence engine (Ical.Net + Noda Time)

| | |
|---|---|
| **Proposed** | Ical.Net for RFC 5545 expansion + Noda Time for zones/DST; no vendor types in public contracts or persisted state; persist DigitalBrain schedule definition only. |
| **User response** | `› its too deep. stop for now and write down all what we have discussed previously…` |
| **Status** | **NOT APPROVED** — explicitly left open; do not implement as settled |

---

## 7. Foundation PoC boundary (post-documentation grilling)

### D7.1 — Lockstep WorkflowRun (see D5.15)

Already ratified above.

### D7.2 — Exact foundation PoC story as decision 1 of final six

| | |
|---|---|
| **Proposed** | Vertical story across Tasks, Time, AI, Google, Salesforce only; fixed exclusions (no Memory/Flutter/tiers/broad MCP/recurring calendar/runtime behaviors); then stop grilling after remaining decisions |
| **User response** | `› approve` in the live continuation after `conversation.txt` |
| **Status** | **RATIFIED** |

---

## 8. Explicitly open / not ratified

Do **not** treat these as settled architecture from this session:

| Item | Why open |
|---|---|
| Exact one-shot Time command/snapshot CLR shapes | Proposed for final approval in `docs/superpowers/plans/2026-07-20-foundation-poc.md` |
| Internal calendar recurrence library (Ical.Net + Noda) | You stopped grilling as “too deep” |
| Memory architecture | Out of scope |
| Member-level vs whole-interface grants as sole model | Superseded by MCP semantic + progressive tools |
| Neutral capability-tool CLR seam | Architecture is ratified; exact records/interface are proposed in the implementation plan |
| Exact `ITask` control methods and orchestration participant records | Domain semantics are ratified; exact CLR shapes are proposed in the implementation plan |
| TDD seam order for Foundation PoC | Proposed for final approval in the implementation plan |
| Google/Salesforce OAuth parameter shapes | Must match current official service contracts when their slices begin |

---

## 9. Compact ratified rule list (implementation cheat sheet)

Use this as a hard checklist. If code contradicts it, the code is wrong unless you reverse the decision in writing.

### Kernel & modules

1. Kernel = neuron mechanics only; no AI/provider/memory/UI domain knowledge. Its one opaque
   `CapabilityDelegation` transport seam is infrastructure, never semantic vocabulary.
2. Modules own vocabulary; behaviors own logic over existing vocabulary.
3. AppHost selects modules once; silo is `AddDigitalBrain()` only.
4. Namespaces and type names are the programming vocabulary.
5. Generated catalogs; no runtime assembly scanning as truth.

### AI / MAF

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

### Integrations / MCP

24. Public interfaces = semantic capabilities (`IGmail`, `ISalesforce`), not toolsets.
25. MCP stays module-private behind pinned catalogs.
26. MAF approval middleware; human authority; agent may only recommend.
27. Progressive tool disclosure by token budget + hybrid retrieval; no hard tool count.
28. `FindCapabilityTools` recovery; no raw string invoke escape hatch.
29. Capability roots may expose no MCP-shaped methods; exact tools remain private/transient.
30. No exactly-once claim; durable dedupe + uncertainty handling for mutations.

### Tasks

31. Independent `DigitalBrain.Tasks` module now.
32. Task = durable desired outcome; MAF Workflow = one Attempt’s execution.
33. Tasks knows nothing about AI/MAF.
34. `IWorker` short requests + attempt facts; only session-owning orchestrations implement it.
35. One active Attempt per Task.
36. Attempt failure ≠ Task failure; terminal Tasks immutable; retries are successors.
37. Small lifecycle + typed blockers.
38. Cooperative truthful cancellation.
39. Fenced async runner; no long MAF work on grain turn.
40. Typed Goal/Result/Failure + fact references; no untyped payload bag.

### Time & hosting

41. Semantic Time ≠ Kernel private timing.
42. Public names: `ICountdown`, `IReminder` (not `ITimer`).
43. One schedule per neuron; explicit destination; revision + CommandId.
44. Interval vs Calendar schedules; deterministic DST; coalesce overdue recurrence.
45. Persisted Time state authoritative; shared Kernel reminder store.
46. First durable profile: single Azure Storage (`WithAzureStorage`).
47. Deterministic test time via `TimeProvider` + simulation driver.

---

## 10. Approval index (user turns → decision)

| User turn (approx.) | Decision |
|---|---|
| `› 1` (×4 plan menus) | D0.1–D0.4 |
| `› we should use … microsoft agent framework` | Redirects D0.5 → §1 |
| `› apptove` | D1.1 AgentSession |
| `› lgtm` | D1.2 AIAgent composition |
| `› approve` | D1.3 MEAI public boundary |
| `› approve, its exactly what i want…` | D1.4 orchestration-by-type |
| `› approve` | D1.5 Concurrent vs GroupChat participants |
| `› yes, approve, … composing existing typed vocabulary` | D2.1 scripts ≠ new types |
| `› approve` | D2.2 one Behavior class |
| `› contract-only behavior is right way` | D2.3 contract-only compile |
| `› Its exactly right direction…` | D2.4 synapse activation |
| `› approve!` | D2.5 dynamic prompts only |
| `› this is much better, approve` | D3.2 semantic capabilities |
| `› approve` | D3.3 MAF approval authority |
| `› approve` | D3.5 token-budget tools |
| `› approve and check which other…` | D3.6 FindCapabilityTools |
| `› approve` | D4.2 durability |
| `› approve` | D4.3 compaction |
| `› approve` | D4.4 observability |
| `› approve` | D4.5 session fingerprint |
| `› approve` | D5.3 ownership split |
| `› yes, lets extract DigitalBrain.Tasks` | D5.4 Tasks module |
| `› approve` | D5.5 IWorker seam |
| `› lgtm` | D5.6 one active attempt |
| `› approve` | D5.7–D5.8 terminal + lifecycle |
| `› approve` | D5.9 cancellation |
| `› approve, but also … ITimer and IReminder` | D5.10 fenced runner + Time |
| `› approve` | D6.1–D6.13 (Time/hosting chain) |
| `› do it` | D6.13 ICountdown naming |
| `› its too deep. stop… document decisions` | Pause; D6.14 open |
| `› approve, but what about implementation…` | D5.15 Lockstep run |
| `› approve` after the exact Foundation story | D7.2 Foundation PoC boundary |
| `› approve` after causal request proposal | D4.7 capability request envelope |
| `› approve` after typed Task vocabulary | D5.16 Goal/Result/Evidence |
| `› approve` after mutation protocol | D4.6 durable mutation/reconciliation |
| `› approve` after capability-root correction | D3.7 private catalog + transient tools |

---

## 11. Source note

Extracted from `conversation.txt` and the live continuation after that export (from the post hard-cut
plan menus through the Foundation PoC implementation-plan transition).

Use this file for approval provenance. Use `REFINED-ARCHITECTURE-AND-NEXT-STEPS.md` and the approved
Foundation implementation plan for current execution. If they disagree, stop and reconcile the two
records rather than inventing a third source of truth.
