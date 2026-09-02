# DigitalBrain v2 — Durable User-Authored Scripting

**Status:** Ratified in conversation; written-spec review pending
**Date:** 2026-09-02
**Branch:** `native`
**Scope:** The first execution vertical after the static neuron substrate.

This is the focused design for merging the durable-run core, user-authored C# scripting, and the
minimum automation trigger path. It refines and, where it disagrees, supersedes the generated-code,
security-admission, and implementation-slice decisions in
[`2026-09-02-digitalbrain-v2-durable-runs-design.md`](./2026-09-02-digitalbrain-v2-durable-runs-design.md).
The durable-run invariants in that umbrella design remain authoritative.

The catalog prerequisite in
[`2026-09-02-digitalbrain-v2-self-knowledge-and-ranked-discovery-design.md`](./2026-09-02-digitalbrain-v2-self-knowledge-and-ranked-discovery-design.md)
is authoritative for descriptors, operation manifests, semantic/lexical indexes, ranked discovery,
and exact inspection. Typed wrappers compile from exact canonical manifests, never search results.

---

## 1. Outcome

From the first executable v2 slice, a user can ask the assistant to create a C# script, receive
compiler diagnostics, revise it, publish it, run it, and connect it to a manual, scheduled, or
external signal. The assistant may revise and republish the script later, then roll connected
automations forward through one idempotent operation. Future triggers use the newly pinned program
revision while already admitted runs remain pinned to the revision with which they started.

The first executor accepts both user-authored and assistant-authored source and runs it as an
ordinary full-trust child process. OS sandboxing, tenant isolation, and hostile-source admission are
explicitly deferred. Process separation exists initially for crash isolation, cancellation, and
unloading—not as a security boundary.

Durability is not deferred. Published automation programs use the `BrokeredSequentialV1` durability
profile: a script can freely perform in-process computation, while every external operation must
cross the host through `StepAsync`. Generated templates and typed tool wrappers therefore route
files, Git, Roslyn, processes, model calls, child agents, notifications, and provider calls through
durable capabilities. A compilation policy rejects known ambient-effect APIs for that profile as a
correctness rule. It is not claimed to resist deliberately evasive full-trust code; hostile-code
enforcement remains deferred.

The acceptance scenario is:

```text
assistant authors script
    → compile diagnostics
    → publish immutable revision
    → recurring/external signal
    → durable run pins revision
    → script performs stable capability steps
    → worker or silo is terminated
    → program restarts from entry
    → completed steps replay from the ledger
    → only unfinished brokered steps execute
```

---

## 2. Decisions and invariants

1. **User and assistant source runs on day one.** There is no origin-based admission block in this
   slice.
2. **The first executor is full trust.** It advertises `FullTrust` honestly; no analyzer,
   `AssemblyLoadContext`, reference list, environment filtering, or process boundary is described
   as a sandbox.
3. **Definitions are mutable only through immutable revisions.** Source is never edited in place
   after publication.
4. **A run pins everything required to reproduce its decisions.** That includes the script
   revision, compilation artifact, capability contract set, input, and resolved context.
5. **The run is the single writer of recovery truth.** Neither the script process, a context entity,
   an activity projection, nor a traffic journal can mark a step complete.
6. **Every external operation in a durable published program receives a stable `EffectId` before
   dispatch.** A worker attempt is never part of that identity.
7. **A worker restart begins at the program entry point.** `StepAsync` replays completed results and
   yields only for unfinished work.
8. **Signals trigger work; synapses do not execute it or grant authority.** Trigger delivery admits
   one run using a stable occurrence identity.
9. **Runtime-created agents and automations are data, not runtime-generated grain types.** Their
   executions are ordinary `ExecutionRun` instances.
10. **There is one execution engine.** The current `ExecutionNeuron` path is retired instead of
    being retained beside the durable engine.

---

## 3. Why the IAW implementation is not ported

The IAW code demonstrates a valuable user experience: produce a typed C# orchestration program,
compile it, return useful diagnostics, repair it, and then run it against tools. DigitalBrain keeps
that experience and the use of Roslyn semantic APIs.

DigitalBrain does not port `CodeOrchestratorAgent`, `IRoslyn`, or `RoslynAgent` as architectural
units:

- `CodeOrchestratorAgent` combines prompting, source generation, filesystem layout, project
  generation, build retry, process execution, progress, and result parsing in one grain.
- Generated IAW projects reference broad client and agent assemblies and can resolve cluster
  proxies directly.
- `dotnet build` and `dotnet run` introduce unrecorded restore/build effects and run generated code
  with ambient host authority.
- Its validator is string rewriting, not a semantic or durable boundary.
- It has no stable command, step, effect, revision, or compilation identities.
- `RoslynAgent` mixes an ephemeral `MSBuildWorkspace`, background loading, LLM analysis, persistence,
  and direct multi-file mutation behind one reentrant grain.

DigitalBrain instead separates definition ownership, deterministic compilation, disposable program
execution, workspace intelligence, and durable capability effects.

---

## 4. Domain ownership

### 4.1 `ScriptDefinition`

`ScriptDefinitionGrain` is a pre-registered aggregate keyed by `(OwnerId, ScriptId)`. It owns:

- display metadata and lifecycle;
- immutable revision descriptors;
- the active published revision pointer;
- command receipts and input fingerprints; and
- a discovery descriptor.

It does not store source, PE files, PDB files, large diagnostics, or logs inline. Those are immutable
artifacts referenced by hash and media type.

Every authoring command carries an `OperationId`. Repeating the same operation and fingerprint
returns the recorded result. Reusing the operation ID with different input is rejected.

### 4.2 Source, compilation, and published program revisions

Authoring uses three immutable records so a failed or repeated compile never mutates source history:

1. `ScriptSourceRevision` contains `ScriptId`, exact source artifact/hash, source origin (`User`,
   `Assistant`, or `Platform`), requested capability contracts, reference-set request, creation
   provenance, and parent source revision.
2. `ScriptCompilationRecord` contains the compilation key, compiler manifest, structured
   diagnostics, emitted assembly/PDB hashes when successful, and terminal compilation outcome.
3. `PublishedProgramRevision` binds one source revision to one successful compilation record,
   exact ABI/reference/capability-wrapper hashes, required executor profile, durability profile, and
   requested capability policy.

`ScriptDefinitionGrain` owns authoring-operation states such as `SourceCreated`,
`CompilationRequested`, `CompileFailed`, `Compiled`, and `Published`; those states describe the
workflow and do not mutate any of the three content records.

Publishing only changes the definition's active `PublishedProgramRevision` pointer. Editing a
published script creates a child source revision. Rolling back republishes a previous valid program
revision without changing its content.

### 4.3 `AutomationNeuron`

`AutomationNeuron` is the pre-registered definition aggregate and stable graph endpoint keyed by
`(OwnerId, AutomationId)`. It handles the stable automation-trigger control signal and is the sole
owner of trigger deduplication; there is no second automation-definition grain. It owns immutable
automation revisions and one active revision pointer. A revision contains:

- a trigger specification;
- a script binding;
- input and context bindings;
- requested capabilities;
- concurrency and overlap policy;
- deadline and retry policy; and
- publication state.

An automation revision pins one exact `PublishedProgramRevision`. Updating a connected script does
not silently change an already published automation. The assistant uses an idempotent
`automation.roll-forward-script` operation to validate compatibility, create a new automation
revision, and publish it. This keeps the requested "future triggers use my edit" workflow while
preserving immutable executable intent and revalidating capability requirements.

### 4.4 `ExecutionRun`

`ExecutionRunGrain` is a pre-registered, non-neuron aggregate keyed by `(OwnerId, RunId)`. It owns:

- immutable `RunSpec`;
- command receipts and fingerprints;
- current run status;
- program attempt records and fencing generation;
- the step/effect ledger;
- compact recovery context and immutable artifact references;
- cancellation and deadline intent;
- parent/child run links;
- pending dispatch and lifecycle outbox entries; and
- terminal result or failure.

It accepts explicit commands. It is not an `INeuron` and does not implement arbitrary
`IHandle<TSignal>` routes. This keeps workflow recovery separate from learned graph routing.

### 4.5 Runs, activities, entities, signals, and synapses

- A **run** is the authoritative active process aggregate.
- An **activity** is a rebuildable public projection over a root run and its descendants.
- An **entity** is passive inspectable state, such as a review verdict or report descriptor.
- An **artifact** is large immutable content addressed by hash.
- A **signal** is an immutable occurrence which may trigger an automation.
- A **synapse** is a graph route for signals; it neither contains script logic nor grants a
  capability.

---

## 5. End-to-end flow

```text
User / assistant
    │ create, revise, compile, publish
    ▼
ScriptDefinition ───────► immutable PublishedProgramRevision + ProgramArtifact
                                  │
Signal / schedule ─► AutomationNeuron
                                  │ resolve exactly once
                                  ▼
                           immutable RunSpec
                                  │
                                  ▼
                         ExecutionRunGrain
                           │            ▲
                 dispatch │            │ step/result commands
                           ▼            │
                    Scripting supervisor
                           │ versioned NDJSON pipes
                           ▼
                   disposable child runner
                           │ StepAsync
                           ▼
                    durable step gateway
                           │
                           ▼
                      EffectWorker
                           │
                           ▼
                  capability implementation
```

The run does not await the child process or a slow provider in a serialized grain turn. It
checkpoints an attempt or effect dispatch, emits a durable outbox item, and yields. Duplicate
dispatches and callbacks are harmless because their identities and fingerprints are already in the
run ledger.

Every aggregate which may own pending work establishes a recurring durable liveness wake before it
commits that work. Immediate Orleans Durable Jobs provide low-latency dispatch; the pre-established
wake is the recovery bridge if a process dies after `DispatchPending` is journaled but before the
immediate job is scheduled. The liveness wake only re-reads state and resubmits recorded work, so
duplicates remain harmless.

---

## 6. Authoring and compilation

### 6.1 Authoring commands

The application surface provides idempotent operations equivalent to:

```text
script.create
script.revise
script.compile
script.publish
script.inspect
script.run
```

`script.revise` always produces a new source revision. `script.compile` first persists
`CompilationRequested(compilationKey)` plus an outbox entry, then returns a receipt. An idempotent
compiler worker stores artifacts with put-if-absent semantics and reports
`CompilationCompleted(compilationKey, outcome, hashes)`. Duplicate reports with matching
fingerprints return the recorded outcome; conflicting reports are rejected. If the worker crashes
after storing artifacts but before its callback, redispatch by the same compilation key discovers
and reports those artifacts.

A failed compilation does not damage the published program revision. Full structured compiler
diagnostics are returned through `inspect`/`observe` so the assistant can issue another source
revision command; the repair loop is visible durable history, not an in-memory retry loop.

### 6.2 Compilation identity

`DigitalBrain.Scripting.Compiler` uses Roslyn `CSharpCompilation` directly. It does not generate a
temporary project, invoke MSBuild, restore packages, or inspect whatever assemblies happen to be
loaded in the current process.

The compilation key hashes canonical representations of:

- exact UTF-8 source bytes;
- Roslyn/compiler version;
- target framework and language version;
- parse and compilation options;
- every metadata reference and file hash;
- scripting ABI hash;
- generated capability-wrapper and schema hashes; and
- compiler policy/configuration version.

Compilation also fixes the virtual source path, deterministic assembly name, metadata-reference
ordering, nullable mode, optimization mode, and path mapping. Compiler time, memory, diagnostic,
source-size, and output-size limits are persisted inputs to the request rather than ambient host
defaults.

The emitted PE and PDB receive their own content hashes. A cache hit is accepted only when the full
manifest and output hashes match. Source, manifest, diagnostics, PE, and PDB are stored as immutable
owner-authorized artifacts.

The initial reference set contains the .NET reference pack, the scripting ABI, and generated typed
capability wrappers. Arbitrary NuGet restore is not part of the first slice because it adds a
separate dependency-resolution effect and lock/reproducibility problem—not because packages are
considered untrusted. A later resolved, content-hashed reference-set artifact can add packages
without changing the execution ABI.

### 6.3 Validation policy

Compilation validation proves protocol compatibility:

- exactly one supported script entry point exists;
- the ABI and capability wrappers match the revision manifest;
- metadata references match the frozen reference set;
- emitted artifacts match their hashes; and
- diagnostics retain ID, severity, path, span, and message.

There is no hostile-code security gate in this milestone. A durability analyzer does reject known
direct filesystem, network, process, provider, ambient time, random, and ID-generation APIs for the
`BrokeredSequentialV1` publication profile because they make replay nondeterministic or bypass the
effect ledger. It permits user- and assistant-authored source equally. This is correctness linting
for cooperative code, not a sandbox or protection against intentionally evasive source.

---

## 7. Scripting ABI

`DigitalBrain.Scripting.Abstractions` is a small Orleans-free assembly:

```csharp
public interface IScriptProgram
{
    ValueTask<ScriptResult> ExecuteAsync(
        IScriptHost host,
        ScriptInput input,
        CancellationToken cancellationToken);
}

public interface IScriptHost
{
    ValueTask<ScriptStepResult> StepAsync(
        ScriptStepRequest request,
        CancellationToken cancellationToken);
}
```

`ScriptStepRequest` contains only script-controlled data:

- stable logical `StepId`;
- capability ID and version;
- input schema ID;
- canonical JSON input; and
- optional compact progress metadata.

Generated code cannot set owner, run ID, lease ID, effect ID, attempt authority, retry generation,
or the observed step ordinal through the scripting ABI. `ReplayScriptHost` assigns a zero-based
ordinal to each sequential `StepAsync` call, and the trusted gateway reconstructs all authority
fields from the admitted attempt. Fields forged into raw payloads are ignored. The first
implementation permits one outstanding step per program attempt. Parallel steps require explicit
ordering and join semantics and are deferred.

Typed wrappers are generated from capability schemas, for example:

```csharp
var snapshot = await tools.Workspace.SnapshotAsync(
    stepId: "snapshot",
    new SnapshotRequest(repository),
    cancellationToken);

var review = await tools.Roslyn.AnalyzeAsync(
    stepId: "analyze",
    new AnalyzeRequest(snapshot.Artifact),
    cancellationToken);
```

Wrappers improve authoring and compilation diagnostics; authority for brokered operations still
comes from the run's capability lease. The full-trust process boundary is not claimed to constrain
malicious ambient OS access.

---

## 8. Worker topology and protocol

The existing `DigitalBrain.Scripting` executable becomes a sibling supervisor service. A separate
`DigitalBrain.Scripting.Runner` executable loads one compiled program revision in a collectible
`AssemblyLoadContext`, executes it, and exits. The ALC supplies deterministic dependency resolution;
process exit supplies unloading. The runner references no Orleans client and no provider
implementation.

The supervisor:

- accepts a versioned start-attempt request from the kernel;
- obtains and verifies immutable program artifacts;
- launches and owns the child process tree;
- bridges the child protocol to the kernel step gateway;
- forwards cancellation and enforces execution deadlines;
- reports exit, completion, diagnostics, and protocol failures; and
- kills stale or superseded attempts.

`StartAttempt` is keyed by `(RunId, AttemptGeneration)` and carries a canonical launch fingerprint
covering the program artifact, protocol and executor profiles, input/context artifact references,
deadline, resource limits, and every other launch parameter. A matching duplicate returns the
existing attempt; the same key with a different launch fingerprint is rejected. The supervisor
keeps a per-attempt registry and places each child in a process group with
kill-on-supervisor-exit/parent-death behavior. Closing the anonymous pipe also causes the child to
exit. Supervisor loss therefore causes the durable attempt dispatch to be redelivered rather than
leaving an intended orphan running beside a replacement.

Supervisor-to-runner communication uses a small length-bounded NDJSON protocol over anonymous
pipes. The runner-side protocol contains only `Ready`, host-assigned-ordinal `StepRequested`,
`ProgramCompleted`, `ProgramFaulted`, `Progress`, and cancellation messages. It contains no run,
owner, lease, or attempt authority. The pipe itself is bound to one admitted attempt.

Kernel-to-supervisor envelopes add protocol version, run ID, attempt generation, pinned artifact
hash, monotonic message sequence, message fingerprint, and kind. Kernel transport may use internal
HTTP, but the transport is not a domain contract. The kernel rejects stale attempts and
fingerprint-conflicting duplicates.

Every `StartAttempt`, `StepRequested`, `ProgramCompleted`, `ProgramFaulted`, `AttemptExited`, and
effect-outcome command has a stable operation ID and fingerprint with a durable receipt at its
aggregate boundary. The supervisor retries an unacknowledged terminal message; a matching duplicate
returns the previous acknowledgement rather than starting a new child or changing terminal state.

The child process shares the host OS principal in the first slice. The supervisor passes no
Orleans/provider connection strings, tokens, or artifact-store credentials, clears unrelated
environment values, and creates a fresh bounded working directory. These measures avoid accidental
coupling; they do not prevent a full-trust process from abusing shared-host authority and are not
described as security.

---

## 9. Durable step and effect protocol

### 9.1 Step identity

At `StepAsync`, `ReplayScriptHost` assigns the request's zero-based ordinal. The run computes an
input fingerprint from canonical JSON version, capability/version, schema, and canonical input;
progress metadata is explicitly excluded. Canonical JSON v1 follows RFC 8785 and its version is
part of every fingerprint.

The run compares request ordinal `N` to its ordered ledger:

- **`N < ledger.Count` and identity/fingerprint match:** return the stored result, or the receipt for
  the same pending step.
- **`N < ledger.Count` and step ID, capability, schema, or fingerprint differs:** fail with
  `DeterministicProgramViolation`.
- **`N == ledger.Count`:** persist exactly one new planned step.
- **`N > ledger.Count`:** reject the invalid trace.

This catches random step IDs, changed branches, skipped calls, and reordered effects after restart;
a newly generated ID cannot appear as an unrelated absent step. A new worker attempt starts its
ordinal at zero and does not create new logical step or effect identities.

`RequestStep` returns immediately after durable admission with either a completed result or a
pending receipt. For a pending receipt, the supervisor makes finite read-only `ReadStep` polls with
bounded backoff; a non-authoritative notification may accelerate the next poll. Every grain query
returns current durable state immediately. No grain call or in-memory continuation remains open
while the effect runs. Reissuing either request after a lost connection is idempotent.

### 9.2 Effect identity and dispatch

Before external invocation, the run persists:

```text
Planned(step, input fingerprint)
Started(step, deterministic EffectId)
DispatchPending(effect invocation)
```

`EffectId` is derived from `(RunId, PublishedProgramRevisionId, ordinal, StepId, capability/version,
schema, input fingerprint, effect generation)`. Effect generation changes only after an explicit
reconciliation/resume decision; program-attempt and infrastructure-retry numbers never participate.

The run dispatches the recorded invocation to one `EffectWorkerGrain` keyed by `EffectId`. Before
crossing the provider boundary, the worker durably records `InvocationMayHaveStarted`. This marker
is intentionally conservative: a crash after it means the call may have happened even when failure
occurred immediately before the actual provider request. The worker then reports one idempotent
outcome command, and the run persists the outcome before exposing it to a script attempt.

Capabilities declare `ReplaySafe`, `Idempotent`, `Reconcileable`, or `NonRecoverable` semantics.
After recovery from `InvocationMayHaveStarted`, replay-safe work may run again, idempotent work
reuses the same effect ID, reconcileable work queries by effect ID before retrying, and
non-recoverable work becomes `Uncertain` without another invocation. The runtime never guesses that
an ambiguous effect is safe to repeat.

The first slice includes a minimum immutable run-scoped capability grant snapshot. Each grant binds
owner, run, published program revision, capability ID/version, operation, schema hashes, resource
scope, and expiry. Both the step gateway and effector validate it. Quotas, delegation, approval
policy, and richer owner policy can expand later; they cannot weaken the recorded run snapshot.

### 9.3 Program recovery

When the child, supervisor, silo, or host restarts:

1. the run replays its durable state;
2. an at-least-once wake-up observes the non-terminal run;
3. a new fenced program attempt starts at `ExecuteAsync`;
4. completed `StepAsync` calls receive stored results;
5. the first missing or pending step is dispatched or awaited; and
6. the script continues from the values returned by the ledger.

Orleans Durable Jobs provides wake-up delivery, not workflow semantics. Duplicate jobs only cause
the aggregate to re-check its ledger.

Program terminal messages are commands too. `ProgramCompleted` is accepted only for the active
attempt and pinned artifact. It includes the final observed call count and transcript hash computed
from every ordinal, step identity, capability/schema, and input fingerprint. Completion is accepted
only when the entire recorded trace was replayed, all ledger entries are terminal-successful, no
step is pending, and the count/hash match. A duplicate with the same canonical output hash returns
the recorded result; a different result for the same completed trace is a deterministic-program
violation.
`ProgramFaulted` and `AttemptExited` distinguish script exceptions, protocol faults, cancellation,
deadline, and infrastructure termination. Retry budget and backoff are immutable run inputs.

### 9.4 Cancellation

Cancellation is persisted intent. Once recorded, the run refuses new steps, requests termination of
the current program attempt, and reconciles any effect already in flight. Killing a process is not
proof that an external effect did not happen.

---

## 10. Signals and recurring automation

A trigger adapter converts manual invocation, a schedule occurrence, webhook, or other event into a
typed signal with stable source identity, payload reference, correlation, and causation.

Before an automation becomes active, `AutomationNeuron` establishes its recurring durable liveness
wake. Immediate due-time jobs are an optimization for precision; the liveness wake guarantees that
an active automation eventually rechecks its persisted schedule even if a crash occurs before the
next job is enqueued.

For a schedule, the logical occurrence identity is derived from automation ID, automation revision,
trigger ID, and scheduled instant. An at-least-once duplicate therefore admits the same run instead
of creating another one. After an occurrence is durably handled, the automation computes and
schedules the next occurrence from persisted schedule inputs. The first scheduler supports
`Skip`, `LatestOnce`, and bounded `CatchUp` misfire policies; the selected policy and catch-up bound
belong to the immutable automation revision. A delayed job for a retired or superseded trigger
revision observes that status and becomes a no-op.

An external trigger uses its verified `SignalId` as occurrence identity. Payload equality is never
used to infer event identity.

On a trigger, the automation aggregate:

1. deduplicates the occurrence;
2. resolves the published automation revision and its exact pinned program revision;
3. applies overlap/concurrency policy;
4. derives a deterministic `RunId` and immutable `RunSpec` containing resolved input, context,
   capabilities, and deadline;
5. persists `AdmissionPending(OccurrenceId, RunId, RunSpecRef, fingerprint)`;
6. idempotently admits that exact run through `ExecutionRunGrain`;
7. persists `Admitted` after the run acknowledges the same fingerprint; and
8. acknowledges handling only after both records are durable.

Every automation wake retries `AdmissionPending` records. A crash before the run commit retries the
same admission; a crash after the run commit but before `Admitted` observes the existing run. There
is no cross-aggregate transaction and no second run identity.

Updating a script changes future direct runs which resolve the script definition's active pointer.
A connected automation changes only after `automation.roll-forward-script` publishes a new
automation revision. Suspending an automation prevents new admissions but does not silently cancel
existing runs.

---

## 11. Future composition: runtime-created agents

This section fixes the extension model but is not implemented in the first merged slice.

An agent is a definition of task behavior; an executing agent is a run. A script can create an
ephemeral child agent through a stable `agent.spawn` capability step whose input freezes:

- task and instructions;
- model policy;
- selected tools/capabilities;
- explicit context manifest;
- parent, correlation, deadline, and result schema.

The effect returns a durable child `RunId`. A later `agent.await` step observes the same child rather
than recreating it. Retrying `agent.spawn` with the same effect identity returns the existing child.

If the user wants reuse, the assistant creates and publishes an immutable `AgentDefinition`
revision. Dynamic creation therefore creates definition and run instances using pre-registered
aggregate types; it never emits an Orleans interface, serializer, or grain class.

This also composes with automation. A PR-review script can:

```text
receive PR signal
  → snapshot repository
  → request Roslyn analysis
  → spawn focused review agents
  → await their durable results
  → assemble verdict
  → publish review through an idempotent/reconcileable capability
```

---

## 12. Roslyn and repository operations

The script compiler and repository intelligence are different services:

- `DigitalBrain.Scripting.Compiler` compiles one immutable script revision with direct Roslyn
  compilation APIs.
- A repository-analysis capability may use `MSBuildWorkspace` outside the silo over a pinned
  workspace snapshot. Its workspace and indexes are rebuildable caches, never grain state.

The useful concepts from IAW's `IRoslyn` become typed capabilities with structured results, not one
large agent returning strings. Initial capability seams include solution/project inventory,
diagnostics, symbol/type lookup, references, callers/callees, and patch production.

Mutation is separated from analysis:

1. analysis or refactoring produces an immutable patch artifact plus expected base hashes;
2. `workspace.apply-patch` is a separately leased effect;
3. application verifies repository scope and preconditions;
4. success records resulting hashes; and
5. retry observes the same resulting hashes or reports a conflict.

Build and test operations similarly run as explicit capabilities and store bounded logs plus artifact
references. They are never ambient calls hidden inside the durable run grain.

---

## 13. Assistant-facing tool surface

The assistant retains four stable meta-operations:

| Tool | Responsibility |
|---|---|
| `discover` | Find definitions, neurons, entities, capability schemas, and invocable operations. |
| `inspect` | Read safe projections, source revisions, diagnostics, runs, activities, and journals. |
| `invoke` | Submit one schema-validated operation with an explicit operation ID. |
| `observe` | Follow a run, activity, artifact, or revision cursor. |

Script creation, revision, compilation, publication, automation authoring, connection, execution,
and cancellation are discoverable operations behind `invoke`. Agent delegation appears behind the
same operation when its later capability pack is installed. The assistant does not need one
permanent top-level tool per provider.

Inside scripts, the published capability catalog produces strongly typed wrappers over `StepAsync`.
Adding Gmail, GitHub, Roslyn, Salesforce, browser, shell, or another integration grows the catalog
without growing the permanent assistant tool surface.

---

## 14. Project and dependency boundaries

The intended project split is:

| Project | Responsibility | Important dependency rule |
|---|---|---|
| `DigitalBrain.Scripting.Abstractions` | Program ABI only | No Orleans, kernel, provider, workspace, or supervisor protocol references. |
| `DigitalBrain.Scripting.Protocol` | Transport-pure supervisor/runner pipe messages with primitive transport IDs | References abstractions only; no kernel, Orleans, or provider contracts. |
| Execution scripting gateway contracts | Internal kernel/supervisor envelopes with run and attempt identity | Never referenced by the runner or generated program. |
| `DigitalBrain.Scripting.Compiler` | Deterministic direct-Roslyn compilation and diagnostics | Roslyn compiler packages only; no MSBuild workspace. |
| `DigitalBrain.Scripting.Runner` | Load and execute one program artifact | References abstractions, transport-pure pipe protocol, and runtime support only. |
| `DigitalBrain.Scripting` | Supervisor service and translation bridge between both protocols | No provider implementations; no credentials are explicitly passed to the child. |
| Execution contracts/module | Definitions, run reducer, ledger, gateway, workers | Does not reference compiler or load generated assemblies. |
| Repository/Roslyn capability module | Pinned workspace analysis and patch artifacts | Runs outside the run grain; caches are disposable. |

An artifact reference is
`(OwnerId, Sha256, Length, MediaType, SchemaVersion)`. Content larger than 32 KiB is never carried
inline in durable state or process messages. Artifact stores implement immutable put-if-absent,
verify hash and length on write/read, and authorize owner access independently of the content hash.

Production uses persistent artifact and Durable Jobs storage. Simulations use deterministic in-memory
implementations of the same interfaces. Orleans Journaling is currently selected by the solution;
Roslyn and a compatible Durable Jobs package still require explicit package selection and API
verification in the implementation plan.

For liveness, runs, compiling script definitions, and active automations register a recurring Orleans
reminder before accepting state which can need future work. Immediate jobs carry a deterministic
`DispatchId` in metadata and can be scheduled more than once. The job handler ignores job identity
for correctness, reads the owning aggregate, and advances only the durable pending record. The
reminder remains until the aggregate is quiescent: terminal or idle with every dispatch,
callback/report, reconciliation, and lifecycle outbox record durably acknowledged and cleared.
Ordering is `persist terminal plus outbox → deliver at least once → persist acknowledgement/clear →
unregister reminder`. A crash after clear but before unregister can leave only a harmless extra wake,
never stranded work.

---

## 15. Migration from the current execution module

The current execution implementation is not extended because it awaits providers and scripts inside
one `ExecutionNeuron` turn and has no durable step/effect ledger. The migration intentionally:

- introduces a new `execution-run` grain identity and new journal state name;
- replaces `ExecutionNeuron`, `ExecutionSession`, `EffectBroker`, `ICapabilityHandler`,
  `IScriptDriver`, `InProcessAllowListedScriptDriver`, `NotImplementedScriptDriver`, and coarse
  `ExecutionState`;
- standardizes the new durable engine on `RunId`; keeps `CapabilityId`, `ContextPath`, and
  `ContextDigest` only where their wire semantics remain valid;
- adapts `ChatTurnWorker` to the new start/read surface instead of leaving both engines alive;
- leaves `IExecutionContext` only as a projection if it remains useful; and
- relocates preferences out of execution ownership.

Existing pre-v2 execution journal state is not silently read as the new aggregate. Before 1.0, the
clean choice is deliberate retirement rather than a permanent compatibility layer.

---

## 16. First merged implementation boundary

The self-knowledge and ranked-discovery plan is an independently testable prerequisite. It supplies
canonical operation/schema manifests plus `discover`/catalog `inspect`; automatic similarity signal
routing remains outside this scripting slice. After that prerequisite, the scripting implementation
plan covers one complete sequential vertical:

1. pure run reducer, command receipts, step/effect ledger, cancellation, and outbox;
2. `ExecutionRunGrain` plus at-least-once wake-up integration;
3. content-addressed artifact abstraction with persistent and in-memory implementations;
4. script definition, immutable source/compilation/program records, durable compile dispatch,
   publish, and run operations;
5. scripting ABI, direct Roslyn compiler, and structured diagnostics;
6. supervisor, disposable runner, versioned protocol, and attempt fencing;
7. brokered sequential `StepAsync` and effect-worker outcomes;
8. minimum automation definition with manual and scheduled/signal triggering;
9. minimum run-scoped grants, typed capability fixtures, one real read-only
   Roslyn/repository-analysis seam, and one
   preconditioned mutation seam;
10. assistant revision-and-repair workflow through the stable brain operations; and
11. retirement of the misleading in-process script path.

Subsequent capability packs add model/agent execution, Git hosting, richer Roslyn operations, email,
browser, and provider integrations. They use the same ABI and ledger. Full multi-agent PR review is
an integration milestone, not a reason to alter the run model.

Deferred from this slice:

- hostile-source sandboxing and restricted-OS executors;
- arbitrary NuGet restore and dependency resolution;
- parallel script steps and joins;
- a general LLM source-generation/repair loop inside the runtime;
- the complete Roslyn workspace capability surface; and
- distributed multi-host worker scheduling.

The assistant itself may still author and repair source through normal application operations from
day one.

---

## 17. Verification

### 17.1 Pure reducer tests

- same operation and fingerprint returns the recorded result;
- same operation with different input is rejected;
- effect IDs are deterministic across worker attempts;
- completed step plus matching fingerprint replays stored output;
- completed step plus different fingerprint fails deterministically;
- a random/reordered step ID at an existing ordinal fails before dispatch;
- program completion with a shorter or different transcript is rejected;
- cancellation prevents the next step;
- terminal state cannot restart; and
- ambiguous non-recoverable outcome becomes `Uncertain`.

### 17.2 Compiler and authoring tests

- user and assistant source origins both compile and can publish;
- equal inputs produce equal compilation keys and output hashes;
- changed source, ABI, wrapper, reference, or compiler inputs change the key;
- diagnostics retain identity and location;
- a broken revision does not replace the active published revision;
- publishing a repaired child revision affects only future runs;
- corrupt or mismatched artifacts are rejected before execution;
- duplicate compilation dispatch/report returns the same immutable compilation outcome; and
- a crash after artifact storage but before compilation callback recovers by compilation key.

### 17.3 Simulation tests

- duplicate wake-ups do not duplicate steps;
- a crash after `DispatchPending` but before immediate job scheduling is recovered by the durable
  liveness wake;
- forced deactivation after step B resumes at step C;
- a provider retry observes the same effect ID;
- a retryable worker failure after `InvocationMayHaveStarted` applies recovery classification
  instead of blindly reinvoking;
- a completed stable step never invokes its external handler again;
- stale program-attempt callbacks are fenced;
- duplicate start dispatch for the active generation starts one runner;
- cancellation between grain turns terminates the attempt and blocks the next step;
- duplicate trigger occurrences admit one run;
- crashes before and after the run-admission commit converge on the same automation occurrence and
  `RunId`;
- an old run remains pinned after a script update;
- outbox redelivery uses the same identity; and
- preconditioned patch replay applies once or reports an explicit conflict.

### 17.4 Process tests

- kill the runner after a completed step, restart from entry, and verify replay;
- kill the scripting supervisor and redeliver the recorded attempt;
- restart the silo against persistent state and resume a non-terminal run;
- crash after terminal state plus outbox commit but before scheduling or acknowledgement and prove
  the lifecycle/result event is redelivered;
- corrupt an artifact and fail before loading it;
- terminate an old fenced attempt and reject its callbacks;
- lose the acknowledgement for a committed program completion and prove redelivery starts no new
  child or effect; and
- prove compiler caches can be deleted and reconstructed from immutable artifacts.

No security-isolation claim or adversarial sandbox test is part of this milestone.

---

## 18. Definition of done

The merged slice is complete when:

- the assistant can create, compile, repair, publish, inspect, and run a user-owned C# script;
- a scheduled or external signal admits exactly one run for one logical occurrence;
- changing and republishing a script changes future direct runs without changing in-flight runs;
- rolling a connected automation forward creates and validates a new immutable automation revision;
- the program can call typed capabilities through stable `StepAsync` IDs;
- terminating the worker after a completed step does not repeat that effect;
- ambiguous effects become explicit `Uncertain` state instead of being guessed or retried blindly;
- the current in-process execution engine has been removed or fully migrated;
- generated assemblies never become grain or wire-contract types;
- the full-trust executor is labelled honestly and no security boundary is claimed; and
- build, unit, simulation, and targeted process-restart suites pass with zero warnings.
