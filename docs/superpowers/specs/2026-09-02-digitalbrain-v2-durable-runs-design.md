# DigitalBrain v2 — Dynamic Agents, Automations, and Durable Runs

**Status:** Ratified in conversation; written-spec review pending  
**Date:** 2026-09-02  
**Scope:** The execution model which follows the kernel rebuild in
[`docs/v2-rebuild-brief.md`](../../v2-rebuild-brief.md).

This specification supersedes D8, D11, D18, §9.3, M4, and the old Slice 2/3 execution story in
[`2026-09-02-digitalbrain-v2-neuron-substrate-design.md`](./2026-09-02-digitalbrain-v2-neuron-substrate-design.md).
It also supersedes the generated-script and runtime-agent walkthroughs in
[`docs/digitalbrain-v2-anatomy.html`](../../digitalbrain-v2-anatomy.html). The vocabulary and static
graph model in those documents remain useful; this document is authoritative when they disagree
about agents, automations, activities, capabilities, generated code, or recovery.

The focused
[`2026-09-02-digitalbrain-v2-durable-scripting-design.md`](./2026-09-02-digitalbrain-v2-durable-scripting-design.md)
brings full-trust user/assistant-authored scripting and a minimal automation path into the first
durable execution slice. It is authoritative where it refines this document's generated-code,
security-admission, worker, and implementation-slice sequencing decisions.

The focused
[`2026-09-02-digitalbrain-v2-self-knowledge-and-ranked-discovery-design.md`](./2026-09-02-digitalbrain-v2-self-knowledge-and-ranked-discovery-design.md)
is authoritative for canonical descriptors, catalog ownership, semantic/lexical projections,
compatible-candidate ranking, exact inspection, and any future similarity-assisted routing.

---

## 1. Goal

An assistant can create, at runtime:

- a one-off agent with a task, instructions, model policy, selected context, tools, deadline, and
  parent;
- a reusable agent definition;
- a durable automation such as pull-request verification; and
- a run of either kind which survives grain deactivation, silo loss, and process restart.

If a PR run has durably completed `clean` and `build`, recovery continues with `test`. It does not
repeat either completed effect. The system never claims exactly-once execution across an external
boundary which cannot supply idempotency or reconciliation.

The design has six invariants:

1. Runtime creation creates instances and immutable definitions, never new Orleans grain types.
2. Definitions describe reusable intent; revisions freeze executable intent; runs own execution.
3. Recovery-critical state and the step ledger have one aggregate owner: the run.
4. A synapse routes a signal. It never grants authority.
5. Every external effect has a stable `EffectId` before it begins.
6. Traffic journals explain what crossed the graph; they are not workflow checkpoints.

---

## 2. What AccedeConcierge proves—and what it does not

[AccedeConcierge at `ed17ea8`](https://github.com/ReubenBond/AccedeConcierge/tree/ed17ea8ad2f37a6604d569845eafbe345b5c4bdc)
demonstrates the right conceptual split:

- durable application collections are rebuilt by replay and committed with explicit
  `WriteStateAsync` boundaries;
- durable tool calls receive stable task identities;
- human approval waits on a durable completion source keyed by request identity; and
- application-level request IDs make retries idempotent.

Its durable tool adapter derives a task ID from the model's tool-call ID and schedules the operation
through an experimental durable-task extension
([`AgentAIFunction.cs`](https://github.com/ReubenBond/AccedeConcierge/blob/ed17ea8ad2f37a6604d569845eafbe345b5c4bdc/src/System.Distributed.AI.Agents/Tools/AgentAIFunction.cs#L54-L101)).
Its approval flow first persists a processed-request ID and then awaits a deterministic completion
grain
([`AdminAgent.cs`](https://github.com/ReubenBond/AccedeConcierge/blob/ed17ea8ad2f37a6604d569845eafbe345b5c4bdc/src/Accede.Service/Agents/AdminAgent.cs#L21-L45)).

It is not a production template for DigitalBrain. It uses .NET 9 and unpublished Orleans 9.2
development/alpha packages. Its streaming chat pump keeps partial output and cancellation state in
memory and persists the response only after the stream completes, so a crash can repeat an LLM or
tool call
([`ChatAgent.cs`](https://github.com/ReubenBond/AccedeConcierge/blob/ed17ea8ad2f37a6604d569845eafbe345b5c4bdc/src/System.Distributed.AI.Agents/ChatAgent.cs#L163-L205)).

DigitalBrain adopts the identities, checkpoints, and idempotency pattern. It does not copy the old
durable-task runtime.

---

## 3. Approaches considered

### 3.1 Chosen: journaled run aggregate plus durable wake-ups

A pre-registered `ExecutionRunGrain` owns journaled run state and advances one durable transition at
a time. A pre-registered `EffectWorkerGrain` performs slow external calls without blocking the run's
serialized command turns. Orleans Durable Jobs wakes or retries runs and workers. A job is only a
durable wake-up; the run ledger decides what work remains and makes duplicate wake-ups harmless.

This uses Orleans for its strengths—virtual identity, serialized turns, replayable state, placement,
and reliable scheduling—while leaving the application's recovery semantics explicit and testable.

### 3.2 Rejected: port Accede's durable async-task runtime

This would make ordinary-looking C# awaits replayable, but the packages used by Accede are old,
unpublished prototypes and are not part of DigitalBrain's Orleans 10.2.2 dependency graph. It also
hides effect identity and ambiguity behind compiler/runtime machinery, where the most important
business invariants are harder to review.

### 3.3 Rejected: restart every handler from its first line

Restarting a handler and relying on all callees to deduplicate is simple, but it gives the run no
authoritative progress, makes cancellation coarse, and cannot explain an ambiguous external
outcome. Restart-from-entry is allowed only for a deterministic program whose every external await
passes through the run's `StepAsync` API; completed step results are then replayed from the ledger.

---

## 4. Domain model and ownership

### 4.1 DigitalBrain and BrainNeuron

`DigitalBrain` is the owner-scoped application boundary and graph. `IDigitalBrain` is its client
facade. `BrainNeuron` is the owner's root/directory neuron. The facade exposes use cases and opaque
references; it never hands an LLM a raw Orleans grain proxy.

`BrainNeuron` registers durable definitions and resolves owner-scoped identities. It does not run
agents, execute automation steps, broker effects, or copy execution state.

### 4.2 Definition aggregates

An `AgentDefinitionNeuron` is a pre-registered grain type keyed by `(OwnerId, AgentId)`. It owns:

- display metadata and lifecycle;
- an ordered set of immutable revision descriptors;
- the currently published revision; and
- a descriptor used for discovery.

An `AutomationNeuron` is a pre-registered grain type keyed by `(OwnerId, AutomationId)`. It is a
graph endpoint which handles the stable `AutomationTriggered` control signal. A dynamic trigger
catalog matches predeployed source-signal aliases and emits that control signal with the original
signal ID, alias, correlation, and payload reference. The automation owns:

- lifecycle: `Draft`, `Active`, `Suspended`, or `Retired`;
- immutable revision descriptors and one active revision pointer;
- trigger declarations and concurrency policy;
- the capability policy requested by each revision; and
- trigger deduplication records.

Changing instructions, program source, trigger rules, requested capabilities, model policy, or
compiler inputs always creates a new revision. It never mutates a published revision. Existing runs
remain pinned to the revision with which they started. Rollback publishes an earlier validated and
approved revision; it does not rewrite history.

Large prompts, generated source, schemas, and results are content-addressed artifacts outside grain
state. A revision stores immutable references, hashes, media types, compiler/analyzer versions, and
contract-set hashes.

### 4.3 Runs are aggregate roots

`ExecutionRunGrain` is a pre-registered grain type keyed by `(OwnerId, RunId)`. It is the sole writer
of recovery-critical run state. A run is an active process aggregate off the learned-routing graph;
it accepts explicit start, cancellation, input, effect-result, and durable-job commands. It is not
an `INeuron`, because arbitrary domain signals are not its public contract. `RunKind` selects a small
injected driver:

- `TaskAgentDriver` interprets an immutable `TaskAgentSpec` and model/tool results;
- `AutomationDriver` interprets an immutable automation plan or a revision-pinned program; and
- later drivers may be added without changing the run state machine.

The grain owns:

- the admitted command ID and request fingerprint;
- parent run, correlation, causation, owner, and deadline;
- an immutable run specification;
- a run-scoped capability lease;
- small recovery-critical context and references to large immutable context;
- the step/effect ledger;
- cancellation intent;
- pending lifecycle signals; and
- terminal output or failure.

A one-off delegated agent is only a `TaskAgentRun`. Creating one does not permanently grow the graph.
If the user asks to reuse it, the assistant creates an `AgentDefinition` revision and future runs pin
that revision.

`EffectWorkerGrain` owns no workflow decisions. Given a recorded `EffectInvocation`, it invokes the
capability broker, durably records that invocation may have started before crossing the provider
boundary, reuses the same `EffectId` across job retries, reconciles ambiguous provider outcomes
where possible, and reports one idempotent outcome command to the owning run.

### 4.4 Activity is a projection

`Run` is the authoritative process aggregate. `Activity` is the public trace/read model for one root
correlation: status, progress, participants, timestamps, current waits, and summarized parent/child
runs. Run snapshots feed the owner's activity projection. Activity is rebuildable, is not a second
writer of execution truth, and does not become a second state machine.

### 4.5 Entities and artifacts

Passive entities remain off the graph. They hold inspectable snapshots such as a PR verdict or a
rendered report. Artifacts hold large immutable content by hash. A run may write an entity only via
an explicit effect with an `EffectId`; storing a context slot is not proof that the external effect
which produced it completed.

---

## 5. Assistant-facing brain operations

The assistant has four stable conceptual tools:

| Operation | Purpose |
|---|---|
| `discover` | Find neurons, definitions, entities, and invocable operations by typed metadata. |
| `inspect` | Read safe projections, schemas, synapses, activity, and journals. |
| `invoke` | Execute one schema-validated application operation using an explicit operation ID. |
| `observe` | Follow a run, activity, journal cursor, or entity revision. |

`discover` is side-effect-free and returns structurally compatible ranked candidates with exact
revisioned handles and rank evidence. It never invokes its top vector result. `inspect` resolves one
or more provisional candidate handles against canonical source state and refuses stale or fabricated
revisions. A durable caller then persists a `SelectionDecision` with the final exact handle, query
fingerprint, structural evidence, policy version, and decision time before `invoke`; semantic rank
alone cannot select automatically.
Admission pins the immutable operation-manifest artifact/fingerprint and binding revision into the
run. Recovery reuses that decision and binding, never repeats semantic search or silently selects a
newer operation. If the pinned binding is unavailable after deployment, recovery returns
`IncompatibleDeployment` deterministically.

`delegate_task`, `create_automation`, `publish_revision`, `connect_synapse`, and domain capabilities
are discoverable operations behind `invoke`; they are not permanent top-level LLM tools. Tool
schemas are produced for the current run and contain only its leased capabilities.

An invocation context contains `OwnerId`, `RunId`, verified principal, operation ID, correlation,
causation, deadline, and capability lease. An operation which lacks that context cannot call an
effector.

---

## 6. Context and capability boundaries

### 6.1 Context

A child agent does not inherit the parent's entire context. `TaskAgentSpec` contains an explicit
`ContextManifest` of immutable references, purpose labels, hashes, and redaction decisions. The
delegation use case creates the manifest before the run starts. A child can return new artifacts or
a result, but it cannot read undeclared parent slots.

Time, random values, generated IDs, model selections, and resolved revision IDs become persisted
inputs the first time they are chosen. Recovery never silently recomputes them.

### 6.2 Capabilities

A run's effective lease is the intersection of:

1. the owner's capability policy;
2. the parent's delegable lease, when there is a parent;
3. the definition revision's requested operations and scopes;
4. provider availability; and
5. user approval and expiry constraints.

A capability is identified by stable ID and version, then narrowed by operation, resource scope,
quota, expiry, and delegation flag. The lease binds to the exact run and, for reusable definitions,
the exact revision hash. A prompt, model response, source file, synapse, or discovered neuron cannot
mint or widen a lease.

Authorization is checked at two boundaries: the run's outbound gateway and the effector/provider.
The second check is mandatory because a raw internal call must not bypass policy.

---

## 7. Effect protocol

Every external call is represented before invocation:

```csharp
public sealed record EffectInvocation(
    EffectId Id,
    RunId Run,
    StepId Step,
    CapabilityLeaseId Lease,
    CapabilityId Capability,
    JsonElement Input);
```

`EffectId` is deterministic from the run ID, stable logical step ID, capability ID, and step
generation. Retries reuse it. The input fingerprint must match the first admitted invocation;
reusing an effect ID with different input is a protocol error.

Every capability declares recovery semantics:

| Semantics | Recovery behavior |
|---|---|
| `ReplaySafe` | The operation is read-only or otherwise safe to repeat. |
| `Idempotent` | The provider accepts `EffectId` as its idempotency key and returns the same outcome. |
| `Reconcileable` | The provider can query the outcome by `EffectId` before retrying. |
| `NonRecoverable` | An ambiguous call becomes `Uncertain`; automatic retry is forbidden. |

Durable automations should normally publish only with `ReplaySafe`, `Idempotent`, or
`Reconcileable` write capabilities. A `NonRecoverable` capability requires explicit approval and a
documented reconciliation action. A generic shell is not granted by default.

Model calls use the same protocol. The model response—including all proposed tool calls—is persisted
before any tool executes. Tool calls then become separate ledger steps. A model provider which
cannot deduplicate or reconcile an ambiguous request can make that model step `Uncertain`, but it
cannot cause already completed tool effects to run again.

---

## 8. Run state machine

### 8.1 Run status

```text
Pending → Running → WaitingForInput → Running → Completed
                  ↘ Failed
                  ↘ Cancelled
                  ↘ Uncertain
```

`Uncertain` is terminal for automatic advancement. A user or reconciler may later record an
authoritative effect outcome and create a new generation which resumes the same run.

### 8.2 Step status

```text
Planned → Started → Succeeded
                  ↘ Failed
                  ↘ Uncertain
        ↘ Skipped
```

The ledger stores stable step ID, generation, effect ID, input fingerprint, status, attempt count,
timestamps, compact output or artifact reference, and failure/reconciliation data.

### 8.3 Checkpoint order

For each effectful step the run:

1. persists `Planned` and its immutable input;
2. persists `Started` and the deterministic `EffectId`;
3. persists a dispatch record and schedules `EffectWorkerGrain` with that same `EffectId`;
4. yields its turn while the worker records `InvocationMayHaveStarted`, invokes the broker, and
   reports a classified outcome;
5. persists `Succeeded` and its result, or a classified failure/ambiguity;
6. atomically advances the run cursor and recovery-critical context; and
7. schedules the next durable wake-up.

The run never executes an effect whose `Started` intent was not acknowledged by
`WriteStateAsync`. It never reports a state transition to its caller before the write which makes
that transition durable completes.

### 8.4 Recovery matrix

| Crash point | Recovered behavior |
|---|---|
| Before `Planned` is durable | Re-plan the step from persisted inputs. |
| After `Planned`, before `Started` | Start the recorded step. |
| Before worker `InvocationMayHaveStarted` | Dispatch the recorded invocation again. |
| After `InvocationMayHaveStarted`, before result is durable | Apply declared recovery semantics: replay, reuse the idempotency key, reconcile, or stop as `Uncertain`. |
| After `Succeeded` is durable | Replay the stored result and advance; never invoke again. |
| After terminal state, before notification delivery | Re-deliver the durable outbox signal with the same `SignalId`. |

This is effectively-once behavior for an idempotent or reconcileable boundary, not a claim of
distributed exactly-once execution.

### 8.5 Wake-up and activation

Orleans Journaling rebuilds the run ledger before requests reach the activation. Orleans Durable
Jobs provides at-least-once wake-up and retry for non-terminal runs and pending effect dispatches.
`ExecutionRunGrain` and `EffectWorkerGrain` treat duplicate jobs as harmless checks of durable
identity and state.

Before accepting state which can need future work, a run establishes a recurring durable liveness
reminder. Immediate Durable Jobs are low-latency wake-ups. The reminder closes the non-atomic gap
where `DispatchPending` is journaled but the process dies before `ScheduleJobAsync`: it eventually
reactivates the run and resubmits the recorded dispatch. Duplicate reminders and jobs only re-check
the ledger. The reminder remains until the run is quiescent: terminal with every dispatch,
reconciliation, and lifecycle outbox record durably acknowledged and cleared. Unregistration comes
last, so a crash can leave a harmless extra wake but cannot strand a committed run or notification.

A run turn only plans, checkpoints, dispatches, or incorporates an outcome; it does not await a slow
provider. A long loop therefore never monopolizes a serialized run turn, and cancellation or user
input can be admitted while an effect is in flight.

Durable Jobs does not hold the continuation, choose the next step, deduplicate effects, or own run
status. Those remain responsibilities of `ExecutionRunGrain` and its ledger.

### 8.6 Cancellation and deadlines

Cancellation is durable intent, not only a `CancellationToken`. Once recorded, no new step may
start. The run requests cancellation of a pending job or active worker, and the worker passes a
token to the provider. The run still reconciles the outcome: provider cancellation does not prove
that an external side effect did not occur. Child runs inherit the earlier of their own deadline and
the parent's deadline. Parent cancellation propagates through durable child links.

### 8.7 Human input

A waiting step persists a deterministic continuation token and expected response schema before it
emits a request. Responses are commands keyed by that token and an operation ID. Repeated responses
with the same fingerprint return the recorded outcome; a different payload for the same operation ID
is rejected. This preserves Accede's durable-completion-source idea without keeping a .NET
continuation in memory.

### 8.8 Output signals

Lifecycle and result signals use a transactional outbox inside the run aggregate. The pending signal
and its stable `SignalId` are journaled with the state transition. Delivery is retried until a
receiver acknowledges the same signal identity. Receiver-side command deduplication makes replay
safe. The traffic journal remains the observable record of each delivery attempt.

---

## 9. Runtime-created task agent

The assistant invokes `delegate_task` with a caller-supplied operation ID. The application service:

1. validates the owner and parent run;
2. resolves and redacts the requested context into a `ContextManifest`;
3. intersects requested capabilities with the parent's delegable lease and owner policy;
4. freezes a `TaskAgentSpec` containing task, instructions, model policy, context, lease, deadline,
   correlation, and parent;
5. derives or allocates one `RunId` idempotently from the operation;
6. persists the run before scheduling it; and
7. returns a safe run/activity reference for `observe`.

The `TaskAgentDriver` makes model calls as ledgered effects. It persists each model response before
executing its tool calls. Tools are generated from the run lease and invoke the capability broker;
they never directly resolve Gmail, Salesforce, filesystem, shell, or another provider.

A task agent may delegate another task only when its lease includes the delegation capability. The
child lease cannot exceed the parent lease, and child context is selected explicitly.

---

## 10. Durable automation

### 10.1 Authoring and publication

Creating an automation produces a draft `AutomationRevision`. Validation checks:

- every trigger and payload is a predeployed signal contract identified by stable alias;
- every operation exists with a compatible schema and recovery classification;
- requested scopes are narrower than owner policy;
- step IDs are stable and unique within the revision;
- compiler, analyzer, contract-set, and source hashes are complete when generated code is used; and
- non-recoverable effects and privilege changes require explicit approval.

Publication atomically selects one validated, approved revision and records outbox mutations for
two separate projections: the exact `TriggerRegistry` and the self-knowledge catalog projection.
Projection delivery is idempotent and at least once; neither projection is part of the aggregate
commit. Handler declarations are capabilities, not synapses. Explicit subscriptions are durable
`Innate` synapses; successful selected deliveries may create or strengthen `Learned` or
`Discovered` synapses only after `DeliveryOutcome.Handled`.

### 10.2 Triggering

A sensor converts a webhook, schedule, or external event into a typed signal with stable
`SignalId`, correlation, causation, verified principal, and payload reference. The
`AutomationNeuron`:

1. deduplicates the trigger signal;
2. snapshots its active revision;
3. evaluates its concurrency policy;
4. derives a deterministic run operation from automation, revision, and trigger signal;
5. creates one `AutomationRun` pinned to that revision; and
6. returns `Handled` only after the run admission is durable.

Schedules and webhooks are sensors. They do not contain automation logic.

### 10.3 PR verification example

The published revision contains stable logical steps:

```text
clean → build → test → [quality-review, architecture-review, test-review] → verdict
```

The three review steps may run as child task-agent runs. The parent records their child run IDs
before starting them and waits on their durable terminal outcomes. After a crash following `build`,
the run replays the `clean` and `build` results from its ledger and starts `test`. A completed child
run is observed, not recreated.

### 10.4 Generated code

Generated C# never becomes an Orleans grain class and never defines wire contracts. Orleans grain
types, proxies, serializers, and manifests remain startup-known. `AutomationNeuron`,
`ExecutionRunGrain`, and `EffectWorkerGrain` remain the graph/runtime citizens.

User- and assistant-authored C# is part of the first durable execution vertical. It implements a
narrow internal program interface in a separate full-trust worker process and uses deterministic
`StepAsync` calls for operations which require durable recovery. Restarting the program from its
entry point is safe because `StepAsync` returns stored results for completed step IDs.

A collectible `AssemblyLoadContext` permits unload inside that worker; it is not a security
boundary. Hostile-source sandboxing and restricted-OS admission are deferred by explicit product
decision. The first worker is labelled `FullTrust`. It may run user or assistant source, while
durability is guaranteed only for effects routed through `StepAsync`.

---

## 11. Orleans and storage boundaries

- `AutomationNeuron`, `AgentDefinitionNeuron`, `ExecutionRunGrain`, and `EffectWorkerGrain` are
  pre-registered types. Runtime work creates virtual grain instances of those types.
- Journaling stores definition metadata, revision pointers, run state, ledgers, and outboxes.
- Durable Jobs supplies at-least-once wake-ups. It is never treated as an exactly-once executor.
- Blob/object storage holds large immutable prompts, source, model transcripts, diffs, logs, and
  reports by content hash.
- Search indexes and owner activity feeds are rebuildable projections.
- Recovery-critical context stays in `ExecutionRunGrain`; a separate context entity may project or
  expose it but is not the proof that an effect completed.
- A run turn never calls its own grain reference. Internal advancement uses the current object or a
  later durable job turn.

Orleans Journaling explicitly replays named durable states before activation completes, and it
requires callers to treat failed writes as uncertain outcomes and use operation identifiers
([runtime behavior](https://dotnet.github.io/orleans/docs/grains/journaling/runtime-behavior/)).
This design applies that rule at both the command and effect boundaries.

---

## 12. Error handling and observability

Failures are classified, not flattened:

- `Rejected`: invalid command, conflicting operation fingerprint, unauthorized capability, or
  invalid revision;
- `Transient`: safe to retry with the same IDs according to policy;
- `Permanent`: recorded failure which does not retry automatically;
- `Cancelled`: durable cancellation observed before the next effect;
- `TimedOut`: durable deadline exceeded;
- `Uncertain`: an effect may have happened but cannot be proven or safely repeated.

Every log, trace, journal entry, activity update, effect, child run, and output signal carries
`OwnerId`, `RunId`, correlation, causation, step ID where applicable, and stable operation/effect
identity. Sensitive arguments and outputs are represented by redacted summaries and artifact hashes.

---

## 13. Implementation slices

This is an umbrella architecture, not one implementation-plan scope. The implementation proceeds as
independently reviewable vertical slices; each slice receives its own focused plan, and any slice
which introduces a new subsystem receives a focused design review first. Slice 1 uses the already
focused `docs/v2-rebuild-brief.md` as its design input.

1. **Static neuron substrate.** Execute `docs/v2-rebuild-brief.md`: split command/query contracts,
   add delivery outcomes, inject `NeuronRuntime`, decompose `Neuron`, collapse delivery paths, gate
   potentiation, and remove or relocate dead/misplaced contracts. No run engine, AI, or codegen.
2. **Self-knowledge and ranked discovery.** Establish canonical module/neuron/signal/operation
   descriptors, exact and lexical lookup, a rebuildable versioned semantic index, compatible
   candidate ranking, and side-effect-free `discover`/exact catalog `inspect`. Automatic semantic
   signal routing remains deferred.
3. **Durable user-authored scripting.** Execute the focused durable-scripting design: replace the
   coarse execution loop with the run reducer and ledger; add immutable script revisions, direct
   Roslyn compilation, a full-trust out-of-process runner, sequential `StepAsync`, durable wake-ups,
   minimal automation triggers, revision pinning, cancellation, outbox, and crash-recovery tests.
4. **Capability packs.** Add the complete lease/policy surface and production capability families,
   including provider idempotency/reconciliation and richer repository/Roslyn operations. The
   merged slice already establishes the effect protocol and minimum real seams.
5. **Task-agent delegation.** Add `TaskAgentSpec`, context manifests, model/tool ledgering, child
   runs, parent cancellation/deadlines, and durable `agent.spawn`/`agent.await` capabilities.
6. **Automation expansion.** Add richer trigger adapters, concurrency policies, approval workflows,
   and the full multi-agent PR verification automation without changing the scripting ABI.
7. **Remaining dynamic discovery and routing learning.** Add owner-directory catalog projection for
   remaining definition/entity kinds, similarity-assisted signal routing, and the correction loop
   without coupling authorization to routing. Script and automation publication already ships with
   the durable-scripting slice.
8. **Isolation and distribution.** Add restricted-OS executors, hostile-source admission,
   dependency resolution, and multi-host scheduling when the product requires those boundaries.

Each slice leaves the full solution green and deployable. No compatibility shim or parallel `v2`
namespace is carried forward.

---

## 14. Verification

### 14.1 Pure state-machine tests

- the same start operation and fingerprint returns the same run;
- the same operation ID with different input is rejected;
- a succeeded step can only replay its stored result;
- cancellation prevents admission of the next step;
- an ambiguous non-recoverable effect becomes `Uncertain`;
- a reconciled effect advances once; and
- a terminal run cannot be restarted by another `Start` command.

### 14.2 Orleans simulation tests

- forced deactivation after step B reactivates and begins step C only;
- duplicate durable jobs do not duplicate a step;
- an idempotent provider sees the same `EffectId` across retry;
- a persisted provider result is applied once after activation recovery;
- cancellation between turns prevents the next effect;
- parent cancellation reaches children;
- duplicate trigger signals create one automation run; and
- an old run stays pinned when a new automation revision is published.

### 14.3 Process-restart tests

An Aspire/Azurite test terminates the silo at controlled crash points, restarts it against the same
journal/job storage, and verifies the recovery matrix. The test provider's idempotency store lives
outside the silo process. Separate cases cover a crash before invocation, after provider acceptance,
after result persistence, and before outbox acknowledgement.

### 14.4 Authority and contract tests

- a child cannot request or use a capability its parent cannot delegate;
- a synapse or discovered operation cannot widen a lease;
- generated code cannot define Orleans interfaces or serialized wire types;
- an effector rejects a missing, expired, wrong-owner, wrong-run, or wrong-revision lease; and
- manifest validation covers every predeployed run/definition wire type.

The first scripting worker is intentionally full trust. These tests protect domain authority and
wire compatibility; they do not claim that the process is a sandbox for hostile code.

---

## 15. Definition of done

The durable-run architecture is complete when:

- a runtime-created task agent has explicit immutable context and least-privilege tools;
- a published automation has immutable, approved revisions and deterministic trigger deduplication;
- a PR run interrupted after `clean` and `build` resumes with `test` and does not repeat completed
  effects;
- every external write either deduplicates/reconciles by `EffectId` or stops as `Uncertain`;
- every non-terminal run is eventually woken after silo/process restart;
- cancellation and deadlines prevent new work and reconcile work already in flight;
- user- and assistant-authored code runs behind stable grain types, and only brokered `StepAsync`
  effects receive durability guarantees;
- the initial executor is identified as full trust without claiming process or ALC isolation is a
  security boundary;
- activity and search views can be rebuilt from authoritative run/definition state; and
- build, unit, simulation, and process-restart suites pass with zero warnings.
