# Subsystem Audit: kernel-runtime (trusted computing base)

- **Subsystem**: kernel-runtime — the `Neuron` base class, journaling runtime, system grains, self-evolution rail, INO conversation/effect runtime, and top-level kernel files.
- **Scope**: files listed in `filelists/kernel-a.txt` (see per-file sections). Commit `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4`, branch `docs/refinement-audit`.
- **Date**: 2026-07-13
- **Finding ID block**: 100-199.

## Subsystem overview

This is the trusted core of NeuroOS. Two distinct durability models coexist here:

1. **Journaled Neuron model** (`Neuron` base class, `Grains/*`, `SelfEvolution/*`) — `DurableGrain` + Orleans Journaling `IDurableList<Synapse>` dual journals (incoming/outgoing). Grains project in-memory state by replaying their journals. This backs the automation/self-evolution/observability neurons.
2. **Encrypted-persistent-state model** (`Runtime/*`) — `Grain` + `IPersistentState<EncryptedRuntimeStateEnvelope>` wrapped by `EncryptedPersistentState<T>` with AES-GCM envelope encryption, HMAC signing, optimistic revision concurrency, and outcome-unknown poisoning. This backs the INO conversation/session/surface-feed/effect-plan runtime.

The self-evolution rail (`SelfEvolutionNeuron` + `ISelfEvolutionApplyHandler` registry) is intended to be the single governed path for autonomous mutation. The INO effect rail (`InoEffectPlan*`, `PlanInoToolGateway`) is a **separate** cryptographically-bound rail for external provider mutations (Gmail send / Salesforce update). Both are allowlisted and fail-closed at the apply/execute boundary. The weakness is authorization of *who* may submit a decision, and durability/atomicity of the journaled apply path.

The encrypted-runtime crypto (`EncryptedPersistentState`, `EncryptedRuntimeStateProtector`, `InoEffectPlanAuthority`) is the strongest code in the subsystem. The journaled `Neuron` base and the system/trigger grains are the weakest (unbounded growth, non-durable dedup, simulation-as-capability, brittle framework coupling).

---

## Per-file review

### `src/DigitalBrain.Kernel.Abstractions/Neuron.cs` (base class — reviewed 1-397)
*(On the file list transitively as "the Neuron base class"; it lives in Kernel.Abstractions but is the core of this subsystem.)*
- **Purpose/value**: Base durable actor. Dual journals, causal stamping (`_currentCause`), FireAsync, checkpoint/branch/restore, timeline subscription, instrumentation.
- **Layer/callers**: Base of every journaled grain in `Grains/`, `SelfEvolution/`. Not used by `Runtime/*` grains (those derive from `Grain`).
- **Correctness**: `FireAsync` journals the stamped synapse then `WriteStateAsync` then delivers. Delivery happens **after** the durable write but is **at-most-once** — a receiver `DeliverAsync` that throws leaves an outgoing-journal record that was never delivered and there is no outbox/redelivery on reactivation (REL-100). Journals grow unboundedly with no compaction (PERF-100). `CreateCheckpointAsync` fires a `Checkpoint` synapse **into the journal**, embedding a dedup of the entire prior timeline; each subsequent checkpoint re-embeds prior checkpoints → superlinear on-disk growth (REL-101). `RestoreCheckpointAsync` **appends** the snapshot to the existing incoming journal without truncation, so restore never shrinks state and can duplicate entries (REL-102).
- **Error handling**: `IsJournalWriterUninitialized` matches a framework internal exception message by substring — brittle coupling to Orleans wording (FRAME-100).
- **Concurrency/lifecycle**: Non-reentrant grain assumption makes the `_currentCause` field-restore correct. Timeline resubscribe via `GetAllSubscriptionHandles`/`ResumeAsync` is the documented Orleans pattern — correct.
- **Perf**: `TryHandleViaDeclaredInterfaceAsync` reflects over interfaces and `MethodInfo.Invoke` on every delivery for grains lacking static dispatch (PERF-101). `SnapshotTimeline` copies the whole journal per query.
- **Framework usage**: `DurableGrain`/`IDurableList` are Orleans 10.2.1 alpha journaling; API shape (WriteStateAsync, IDurableList.Add) is consistent with the preview. Microsoft Learn confirms the journaling preview is experimental (`ORLEANSEXP005`); no version-specific durability contract for `IDurableList` truncation is documented — **documentation gap** noted; the unbounded-growth risk is inherent, not a misuse.
- **Verdict**: retain but **simplify + bound**. Add journal compaction/snapshot-truncation; make checkpoint storage out-of-band (use `CheckpointProtector`, not an in-journal synapse); document at-most-once delivery or add an outbox.
- **OS model**: The durable/replayable/rollback claims of the OS rest on this file; the growth and at-most-once gaps weaken those claims for the journaled path.

### `src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionNeuron.cs` (reviewed 1-183)
- **Purpose/value**: The governed rail. Validates proposals, records decisions, dispatches approved proposals to allowlisted apply handlers, projects pending/decided/applied/expired sets from journals.
- **Correctness**: Idempotency by `ProposalId` across pending/decided/applied/expired sets — replay of the same decision is rejected. `RebuildProjection` deterministically replays journals on activation (`ShouldSubscribeToTimeline=false`, point-to-point only) — good.
- **Security (critical)**: `HandleAsync(SelfEvolutionDecision)` only checks `DecidedBy` is non-empty. There is **no authorization** that the caller is entitled to approve, and `DecidedBy` is an attacker-controlled free string with no cryptographic binding (SEC-100). The proposal's `RequiresHumanApproval` flag is **never read** — an approved decision applies regardless of whether human approval was required (SEC-101).
- **Reliability (partial apply)**: On approve, `_decided.Add` runs before apply; if `_applyRegistry.ApplyAsync` returns `Succeeded=false` (including after partial side effects), the proposal is now terminally "decided" and a re-submitted decision is rejected ("already decided"). Handlers that never set `RollbackCheckpointId` (all of them) produce no `SelfEvolutionRollbackRequired`. Net: a partially-applied proposal is unrecoverable and non-retriable (REL-103).
- **Verdict**: retain but harden — require an authenticated approver principal, honor `RequiresHumanApproval`, and separate "decision recorded" from "apply outcome" so a failed apply can be retried or explicitly rolled back.
- **OS model**: This is the sacred rail; SEC-100/101 and REL-103 are the most important trust findings in the subsystem.

### `src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionApplyHandler.cs` (reviewed 1-73)
- **Purpose/value**: `ISelfEvolutionApplyHandler` contract + `SelfEvolutionApplyRegistry`.
- **Correctness/security (positive)**: Fail-closed and allowlisted — unknown `ApplyVia` → Failed; duplicate handlers for one `ApplyVia` → Failed (prevents duplicated authority); `proposal.Risk > handler.MaxRisk` → Failed; handler exceptions caught → Failed; result normalized to the proposal's id/applyvia. This is the correctly-designed gate.
- **Gap**: no rollback/compensation contract — a handler that mutates then throws leaves side effects (feeds REL-103).
- **Verdict**: retain.

### `src/DigitalBrain.Kernel/AutomationDefinitionApplyHandler.cs` (reviewed 1-93)
- **Purpose/value**: Two apply handlers (define/remove automation reaction) at `InProcessCode` risk.
- **Correctness**: Reads the staged definition from the origin automation neuron's outgoing timeline by `ProposalId`, then fires `RegisterScript` + `RegisterReaction` + `CapabilityRegistered` (define), or `RemoveReactionAsync` + `CapabilityRegistered` (remove).
- **Reliability**: Define fires three synapses sequentially with no atomicity; a failure between them leaves a registered script with no reaction (or capability), and per REL-103 cannot be retried (REL-104).
- **Security**: `proposal.Origin` is used directly as the grain key with only a non-empty check — an approved proposal can target an arbitrary `IAutomationNeuron` id (low impact; typed grain).
- **Verdict**: retain; make define transactional or idempotent-on-retry.

### `src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs` (reviewed 1-366)
- **Purpose/value**: Reactive host for automations (scripts + reactions), always timeline-subscribed, journal-sourced.
- **Correctness**: `EnsureProjections` rebuilds only when both projections are empty — after a live mutation the cached projection is trusted; on reactivation it replays journals. Matching logic (`IsMatch`/`TargetMatches`/`ScopeMatches`) is heuristic (substring/prefix) and defaults loose ("return true // default loose for compat") — over-broad matching is possible (REL/Note).
- **Security**: `DefineReactionAsync` bypasses the approval rail by design (documented as trusted-bootstrap only) but is a **public interface method** on `IAutomationNeuron` — any caller with the grain reference bypasses self-evolution (SEC-102).
- **Perf**: `EnsureProjections` and execution replay `OutgoingJournal.Concat(IncomingJournal).OfType<...>()` — O(journal) per activation; unbounded (PERF-100 dependency). Script execution routed through `Foundry.ScriptRunner` with capability broker.
- **Verdict**: retain; tighten match defaults and gate/annotate `DefineReactionAsync`.

### `src/DigitalBrain.Kernel/Grains/GeneratedNeuron.cs` (reviewed 1-368)
- **Purpose/value**: Dynamic host for embodied packs; also serves a hardcoded "Gmail insights" demo experience.
- **Dead code**: `LastInstalledPack()` unconditionally returns `null`, so the entire LLM-embodied-pack branch after `if (inst is null) return;` in `UseExperienceAsync` is unreachable (CLEAN-100).
- **Placeholder-as-capability**: `RunGmailInsightsExperienceAsync` builds `BuildGmailSampleRows(100)` fake data and presents it as "analyzed locally" (PROD-100).
- **Correctness**: `NormalizePackOutput` nulls `CorrelationId`/`CausationId` and reissues `SynapseId`, severing causal lineage for pack emissions (Note — likely intentional isolation, but breaks traceability).
- **Verdict**: simplify — delete the dead installed-pack path; move sample data behind an explicit demo flag.

### `src/DigitalBrain.Kernel/Grains/SystemNeurons.cs` (reviewed 1-178)
- **AspireOrchestratorNeuron**: `PerformKernelSelfUpdate` is a **simulation** — hardcoded `replica 1..3`, `RestartResource` only logs, UiSurfaces assert `haReplicas:3`/`draining-replica`. No real Aspire orchestration (PROD-101). It also exercises the trusted-core `CreateCheckpointAsync`/`RestoreCheckpointAsync` (so REL-101/102 apply to the "rollback" path). `cmd.FailAtReplica` is a test-only failure-injection field baked into a production handler (CLEAN-101). `KernelPack.Description = "…rolling replica support"` is aspirational.
- **ObservabilityNeuron / MetaOptimizerNeuron**: journal-scan projections (`Concat(...).OfType<...>()`) are O(journal) per message (PERF-100). `MetaOptimizerNeuron` fires `WiringOptimizationProposed` that nothing routes into the self-evolution rail — a speculative dead-end (CLEAN-102).
- **Verdict**: PerformKernelSelfUpdate — replace or clearly quarantine as demo; extract `FailAtReplica`.

### `src/DigitalBrain.Kernel/Grains/SystemRollingSurfaces.cs` (reviewed 1-83)
- Pure UiSurface builders for the rolling-update demo. No logic risk. Hardcoded "/3" and "3 replicas" strings reinforce the simulation (supports PROD-101). Verdict: retain (or delete with PerformKernelSelfUpdate).

### `src/DigitalBrain.Kernel/Grains/LlmNeuron.cs` (reviewed 1-25) & `LlmResponderNeuron.cs` (reviewed 1-93)
- Thin chat-client wrappers. `LlmResponderNeuron` caches scoped clients per (provider,key) and resolves a user-selected provider from `IPackConfigStore` with a swallowed generic catch (`catch { /* config optional */ }`) — acceptable but hides config errors (Note). Correct cancellation rethrow pattern. Verdict: retain.

### `src/DigitalBrain.Kernel/Grains/PollTriggerNeuron.cs` (reviewed 1-113)
- **Reliability bug**: `_seen` dedup HashSet is in-memory; the comment claims it is "enhanced from journals on replay" but `EnsurePolls` never rebuilds `_seen`. After reactivation `_seen` is empty and every previously-seen poll item re-fires as new (REL-105). Reminder period respects Orleans 1-min minimum.
- **Security/Note**: `broker.HttpGetAsync(source)` on any `http…` target from a reaction — SSRF surface bounded by the capability broker and the approval rail.
- **Verdict**: persist dedup cursor to the journal.

### `src/DigitalBrain.Kernel/Grains/ScheduleTriggerNeuron.cs` (reviewed 1-99)
- **Functional gap**: the `Schedule` cron expression is stored but ignored — reminders use a fixed 5s due / 1-min period, so every scheduled reaction fires every minute regardless of its cron (REL-106). Comment openly admits "cron parsing … when needed."
- **Verdict**: implement cron→next-due, or rename the field to reflect fixed cadence.

### `src/DigitalBrain.Kernel/Kernel/CheckpointKeyProviders.cs` (reviewed 1-13)
- Reads AES key from config; `Convert.FromBase64String` throws on malformed config (fail-fast at startup — acceptable). Verdict: retain.

### `src/DigitalBrain.Kernel/Kernel/CheckpointProtector.cs` (reviewed 1-22)
- Orleans-serializes the `Checkpoint.Snapshot` then AES-protects it via `INeuronStateProtector`. Correct use of the polymorphic Orleans serializer. **Not** invoked by `Neuron.CreateCheckpointAsync` (which instead journals a raw Checkpoint synapse) — the safe path exists but the base class doesn't use it (supports REL-101). Verdict: retain; route base-class checkpointing through this.

### `src/DigitalBrain.Kernel/Kernel/JournalJson.cs` (reviewed 1-88)
- Configures `System.Text.Json` polymorphism for `Synapse` by reflection-scanning DigitalBrain assemblies; fail-closed on unknown discriminators (`FailSerialization`, `IgnoreUnrecognizedTypeDiscriminators=false`). **Redundancy concern**: the encrypted host also registers `EncryptedSynapseJsonConverter` (a `JsonConverter<Synapse>`) on the same options; a converter overrides polymorphism options, so in the encrypted production path this polymorphism config is effectively dead (CLEAN-103). Verdict: keep for the non-encrypted path; document precedence.

### `src/DigitalBrain.Kernel/Kernel/KernelServices.cs` (reviewed 1-41)
- `AddKernelSecurity`: AES-GCM protector when a key is present; **Production fails fast** without a key; dev falls back to `PassThrough` with a loud warning. Correct fail-closed-in-prod posture. Verdict: retain.

### `src/DigitalBrain.Kernel/Kernel/KernelTaskSynapses.cs` (reviewed 1-13)
- `IKernelTask` interface has **no implementing grain and no callers** anywhere in the repo (grep-confirmed) — dead speculative contract (CLEAN-104). Verdict: delete.

### `src/DigitalBrain.Kernel/Program.cs` (reviewed 1-35)
- Minimal web host bootstrap. `ForwardedHeaders` correctly gated: in Production it only clears known-proxy restrictions when `TrustAzureContainerAppsIngress=true`, `ForwardLimit=1`. `OAuthTransportBoundary` middleware first. Verdict: retain.

### `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` (reviewed 1-80) & `Dockerfile` (reviewed 1-27)
- `net11.0`, `NoWarn` for `ORLEANSEXP005`/`MEAI001` (documented). Wide dependency surface (Anthropic, OpenAI, Azure, Roslyn, MCP, Orleans, Grpc) pulled into the trusted core — large blast radius (Note). Dockerfile uses `.NET 11 preview` base images and copies the whole repo (`COPY . .`) before restore (build-cache inefficiency; Note). Trailing blank lines in csproj (cosmetic). Verdict: retain.

### `src/DigitalBrain.Kernel/appsettings.json` / `appsettings.Development.json` (reviewed in full)
- Logging + CORS allowlist (prod origins) + Aspire tracing toggles. No secrets. Verdict: retain.

### `src/DigitalBrain.Kernel/Runtime/EncryptedPersistentState.cs` (reviewed 1-478)
- **Purpose/value**: The encrypted-state engine — `RuntimeStateKeyRing`, `EncryptedRuntimeStateProtector` (AES-GCM envelope: random DEK per write, KEK-wrapped, HMAC-SHA256 signature over AAD+envelope, purpose-separated AAD), and `EncryptedPersistentState<T>` (optimistic revision concurrency, validate-on-read/write, KEK rewrap-on-read, single-semaphore serialization, poisoned-on-outcome-unknown).
- **Security (positive/strong)**: `FixedTimeEquals` everywhere, `ZeroMemory` of plaintext/DEK, signing-key must be distinct from every KEK, exact envelope metadata validation, `MaxDepth`/`UnmappedMemberHandling.Disallow`, ciphertext bound (4 MiB). Revision must advance exactly +1. Correct handling of the outcome-unknown case via `_poisoned` (activation refuses further use until reactivation).
- **Correctness**: `UpdateAsync` reuses the opened DEK when present, else generates one; rewrap path re-seals under the active KEK. Solid.
- **Verdict**: retain — reference-quality. Minor: `MaximumCiphertextBytes` is a hard cap that will surface as a runtime throw for large conversation state (bounded by design).

### `src/DigitalBrain.Kernel/Runtime/PersistedStateReconciliation.cs` (reviewed 1-55)
- Write-with-recovery: on write failure it re-reads to distinguish "already committed" vs "safe rollback" vs "advanced/unknown", throwing `PersistedStateWriteOutcomeUnknownException` when the recovery read also fails. This is the correct answer to lost-acknowledgement partial-write ambiguity. Verdict: retain — exemplary.

### `src/DigitalBrain.Kernel/Runtime/EncryptedSynapseJsonConverter.cs` (reviewed 1-143)
- Per-synapse AES envelope with an **allow-listed** type table (unknown type → `JsonException`), size bounds, `ZeroMemory`. `_nextRevision` starts random and increments; on reactivation a fresh converter restarts the counter, but the revision is only an AAD component bound by signature per entry (no cross-entry replay protection is claimed; the journal blob is itself under Orleans storage). Verdict: retain.

### `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs` (reviewed 1-388)
- **Purpose/value**: Durable conversation aggregate — inbox/outbox, operations state machine, archive segmentation, reminder+timer drive of scheduled operations.
- **Correctness**: Legacy migrations run on activation (accepted-commands, outbox sequences, authorization-payload scrubbing) guarded by predicates. `HasOperationToWatch` drives reminder lifecycle; idle → unregister. Archive verification throws `RuntimeStateIntegrityException` on segment mismatch. Optimistic `expectedRevision` throughout.
- **Concurrency**: Reminder (1 min, durable) + grain timer (fast) combo keeps latency low while surviving deactivation — sound.
- **Verdict**: retain — well-constructed; high complexity is inherent to the guarantees.

### `src/DigitalBrain.Kernel/Runtime/ConversationArchiveNeuron.cs` (reviewed 1-55)
- Immutable, single-write archive segment; key-bound (`SegmentId` must match grain key), idempotent `PutAsync` (same segment → no-op, changed → integrity throw). Verdict: retain.

### `src/DigitalBrain.Kernel/Runtime/ConversationModelGrain.cs` (reviewed 1-149)
- Structured intent/mutation extraction via `IChatClient` with bounded prompt (4096) and grounding cap (12). Extensive prompt-injection guidance (tool output is "authoritative, untrusted"; never emit provider identifiers/SOQL/tokens). Uses JSON-schema response format. Verdict: retain — the guidance is the security boundary against the model; solid but only as strong as model compliance (inherent).

### `src/DigitalBrain.Kernel/Runtime/InoEffectPlanAuthority.cs` (reviewed 1-206)
- HMAC-SHA256 plan tokens and execution proofs binding planId/actorScope/toolId/summaryDigest (and operationId/effectId/idempotencyKey for execution). Strict format validation, `FixedTimeEquals`, base64url round-trip verification. Verdict: retain — strong.

### `src/DigitalBrain.Kernel/Runtime/InoEffectPlanNeuron.cs` (reviewed 1-276)
- Single-shot external-mutation executor. Validates plan binding + execution proof, idempotent completion (replays stored result), expiry via reminder, allowlists Gmail.Send/Salesforce.UpdateRecord (else integrity throw). Provider status → safe disposition mapping including `OutcomeUnknown` for unconfirmable results; completion persisted with `CancellationToken.None` so an in-flight cancel can't lose the recorded outcome. Verdict: retain — this is the correctly-built mutation rail.

### `src/DigitalBrain.Kernel/Runtime/InoEffectPlanStore.cs` (reviewed 1-49)
- Mints random 256-bit planId, validates plan, persists, returns a signed `InoToolRequest`. Verdict: retain.

### `src/DigitalBrain.Kernel/Runtime/InoConversationOutboxDispatcherGrain.cs` (reviewed 1-273)
- Ordered, at-least-once outbox → surface-feed projection with identity verification, idempotent projection ids, bounded (attempt<3) revision-conflict retries, tombstone/identity guards. This is a real durable outbox (contrast the `Neuron` base). Verdict: retain.

### `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs` (reviewed 1-1233)
- The INO operation state machine: lease fencing (`LeaseOwner`+`Attempt`), optimistic concurrency, bounded reconciliation (8 attempts), deadline cancellation, and a strict invariant that the model/provider is **never re-invoked after a result is observed** — re-reads and persists the same result on conflict. Authorization resume, approval request, effect execution, retry backoff, and outcome-unknown are all handled with safe user-facing text. `RequiredOperation` (line 1150) appears unused (Note/CLEAN). Verdict: retain — the most complex file and, from inspection, carefully correct on idempotency.

### `src/DigitalBrain.Kernel/Runtime/ClosedInoToolGateway.cs` (reviewed 1-23) & `PlanInoToolGateway.cs` (reviewed 1-57)
- `Closed` denies everything (production default when `DigitalBrain:Tools:Enabled` is false — verified in hosting). `Plan` authorizes only allowlisted mutation tools with a valid signed plan token and issues an execution proof before delegating to `InoEffectPlanNeuron`. Fail-closed. Verdict: retain.

### `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs` (reviewed 1-778)
- Thin Agent Framework adapter (`ChatClientAgent`, `CreateSessionAsync`, `RunAsync`) that first attempts typed Gmail/Salesforce reads/mutations before falling back to a free-form agent turn. Heavy output sanitization (`UnsafeProviderField`, `SafeText`, URL stripping, bounds). Mutations always route through `IInoEffectPlanStore.PrepareAsync` (never executed inline). Verdict: retain. Note: the Microsoft.Agents.AI 1.13.0 session/run APIs are used minimally; Orleans remains lifecycle authority as documented.

### `src/DigitalBrain.Kernel/Runtime/SessionNeuron.cs` (reviewed 1-72) & `SurfaceFeedNeuron.cs` (reviewed 1-108)
- Thin encrypted-state grains delegating to `*Transitions` domain logic (refresh rotation, revoke, action-binding consumption, projection). Consistent optimistic-concurrency pattern. Verdict: retain.

---

## Answers to subsystem-specific questions

1. **Neuron journaling / bounds**: Dual `IDurableList<Synapse>` journals, `FireAsync` = journal + `WriteStateAsync` + deliver, causal lineage via `Stamp`/`CorrelationId`/`CausationId`, checkpoints via an in-journal `Checkpoint` synapse, `RestoreCheckpointAsync` = append snapshot to incoming journal. **The journal is unbounded** — no compaction/truncation anywhere (PERF-100), and checkpoints embed the whole timeline into the journal, so growth is superlinear (REL-101). Orleans journaling preview docs (Microsoft Learn) do not document a truncation contract for `IDurableList`; this is a real, un-mitigated growth risk, not a doc gap in usage.

2. **Single authoritative rail?** For *journaled in-process* mutation the `SelfEvolutionNeuron` + allowlisted, fail-closed `SelfEvolutionApplyRegistry` is the rail — but `IAutomationNeuron.DefineReactionAsync` is a public bypass (SEC-102), and any grain that fires `RegisterScript`/`RegisterReaction` directly at an automation neuron also bypasses it. For *external provider* mutation the separate `InoEffectPlan`/`PlanInoToolGateway` rail is cryptographically bound and fail-closed (default gateway denies all). So there are **two** rails, both fail-closed at apply/execute, plus at least one in-process bypass.

3. **Checkpoint/rollback replay-safety**: `RestoreCheckpointAsync` seeds the incoming journal without re-dispatch (state recovery, not re-execution) — that intent is correct, but it **appends** rather than replaces, so it neither shrinks state nor prevents duplication (REL-102). Partial apply in the self-evolution rail has **no rollback** (handlers never set `RollbackCheckpointId`) and is non-retriable once decided (REL-103/104). The INO effect rail, by contrast, has correct outcome-unknown handling (`PersistedStateReconciliation`, `InoEffectPlanNeuron`, `InoOperationWorkerGrain`).

4. **Grain keying/activation/reentrancy/cancellation**: Journaled neurons rely on Orleans non-reentrancy for the `_currentCause` field pattern (correct). Cancellation is propagated (`ThrowIfCancellationRequested`) but `FireAsync` can throw *after* the durable journal write, producing a journaled-but-undelivered synapse with no redelivery (REL-100). `PollTriggerNeuron` loses its dedup set across activations (REL-105). The `Runtime/*` grains use optimistic revision + lease fencing correctly.

5. **`AspireOrchestratorNeuron.PerformKernelSelfUpdate`**: **Simulation/placeholder**, not real orchestration — hardcoded 3 replicas, `RestartResource` only logs, UI props assert drain/verify/rollback phases, and `FailAtReplica` is a test hook in the production handler (PROD-101, CLEAN-101). It does exercise the real (flawed) checkpoint/restore path.

6. **Authorization on self-evolution decisions**: **None beyond a non-empty `DecidedBy` string** (SEC-100). `DecidedBy` is unauthenticated and forgeable; `RequiresHumanApproval` is ignored (SEC-101). Replay of the *same* proposalId is blocked, but a forged decision for any pending proposal is accepted.

---

## Findings

### SEC-100: Self-evolution decisions are unauthenticated and forgeable
- **Severity**: Critical | **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionNeuron.cs:54-66` — the only decision gate is `if (string.IsNullOrWhiteSpace(decision.DecidedBy)) reject`. No principal authorization, no signature.
- **Current behavior** (FACT): Any caller able to deliver a `SelfEvolutionDecision` to the rail with an arbitrary `DecidedBy` string and a valid pending `ProposalId` causes the approved apply handler to run.
- **Why it matters** (INFERENCE): The journal records an approver identity that cannot be trusted; there is no cryptographic or capability binding between the decider and an authorized human/principal.
- **OS/product consequence**: Breaks the "human-approved" trust boundary of the self-evolution rail — the single most important governance invariant.
- **Recommendation** (PROPOSAL): Require an authenticated approver token/capability (bind `DecidedBy` to a verified principal, e.g. reuse `InoEffectPlanAuthority`-style HMAC or a session/grant check) and reject decisions lacking it.
- **Deletion/simplification**: no.
- **Dependencies**: SEC-101, REL-103; MCP/gateway subsystem (who submits decisions).
- **Tests/measurements**: Test that a decision with an unauthorized/forged principal is rejected; that only an authorized principal approves.
- **Effort**: M
- **Migration/rollback**: Additive validation; may require a decision-token issuance path.

### SEC-101: `RequiresHumanApproval` is never enforced
- **Severity**: High | **Confidence**: High
- **Evidence**: `SelfEvolutionNeuron.cs` (whole `HandleAsync(SelfEvolutionDecision)`) never reads `proposal.RequiresHumanApproval`; the field is declared at `src/DigitalBrain.Core/SelfEvolution.cs:32`.
- **Current behavior** (FACT): An approved decision applies regardless of whether the proposal demanded human approval.
- **Why it matters** (INFERENCE): A flag that exists to force a human gate is dead; automation can approve automation-required-human changes.
- **OS/product consequence**: Weakens the approval rail; a proposal marked human-only can be auto-approved by any decision source.
- **Recommendation** (PROPOSAL): When `RequiresHumanApproval`, require an authenticated *human* principal class on the decision; otherwise reject.
- **Deletion/simplification**: no (or delete the field if truly unused — but it should be enforced).
- **Dependencies**: SEC-100.
- **Tests/measurements**: Proposal with `RequiresHumanApproval=true` rejects a system-principal decision.
- **Effort**: S
- **Migration/rollback**: Additive.

### SEC-102: `IAutomationNeuron.DefineReactionAsync` bypasses the approval rail
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs:277-285` fires `RegisterScript`/`RegisterReaction` directly; comment concedes it "bypasses the approval rail."
- **Current behavior** (FACT): Any caller with the grain reference registers executable automation without a `SelfEvolutionProposal`.
- **Why it matters** (INFERENCE): "Trusted bootstrap only" is a convention, not an enforced boundary; the method is on the public grain interface.
- **OS/product consequence**: A second, ungoverned path to install in-process reactive code.
- **Recommendation** (PROPOSAL): Move bootstrap registration to an internal/trusted-only surface, or require a trust flag; keep the public path proposal-gated.
- **Deletion/simplification**: possibly delete if unused by trusted callers.
- **Dependencies**: SEC-100.
- **Tests/measurements**: Assert public automation definition requires an approved proposal.
- **Effort**: M

### REL-100: `FireAsync` is at-most-once — journaled but possibly undelivered
- **Severity**: High | **Confidence**: Medium
- **Evidence**: `Neuron.cs:195-219` — writes to out-journal + `WriteStateAsync`, then `target.DeliverAsync`; a delivery exception leaves the journal record with no redelivery on reactivation.
- **Current behavior** (FACT): Point-to-point delivery has no outbox/retry; the outgoing journal is a record of *intent to send*, not confirmed delivery.
- **Why it matters** (INFERENCE): The "durable, replayable" OS claim does not hold for the base journaled path; a crash between journal write and delivery silently drops the message.
- **OS/product consequence**: Cross-neuron causal chains can break without signal; contrast the correct `InoConversationOutboxDispatcherGrain`.
- **Recommendation** (PROPOSAL): Either document base delivery as best-effort, or add an outbox/redelivery driven by the outgoing journal + acknowledgement.
- **Deletion/simplification**: no.
- **Dependencies**: PERF-100.
- **Tests/measurements**: Kill between journal write and delivery; assert redelivery on reactivation (currently fails).
- **Effort**: L

### PERF-100: Neuron journals grow unboundedly; projections are O(journal) per message
- **Severity**: High | **Confidence**: High
- **Evidence**: `Neuron.cs` (no truncation anywhere); `AutomationNeuron.EnsureProjections`, `ObservabilityNeuron.PublishGraphFromJournalAsync`, `MetaOptimizerNeuron.HandleAsync` all `OutgoingJournal.Concat(IncomingJournal).OfType<...>()`.
- **Current behavior** (FACT): Every fired/received synapse is retained forever; hot neurons re-scan the full journal per activation/message.
- **Why it matters** (INFERENCE): Latency and storage grow without bound; long-lived neurons (automation, observability, optimizer) degrade.
- **OS/product consequence**: Undermines durability at scale; replay cost is unbounded.
- **Recommendation** (PROPOSAL): Add journal compaction/snapshotting (fold to a checkpoint, truncate consumed prefix) and cache projections incrementally.
- **Deletion/simplification**: yes — compaction reduces stored volume.
- **Dependencies**: REL-101.
- **Tests/measurements**: Measure journal length + activation time under N=10^5 synapses.
- **Effort**: L

### REL-101: Checkpoints embed the full timeline into the journal (superlinear growth)
- **Severity**: High | **Confidence**: High
- **Evidence**: `Neuron.cs:247-256` — `CreateCheckpointAsync` builds a dedup snapshot of `OutgoingJournal.Concat(IncomingJournal)` and `await FireAsync(cp)`; the `Checkpoint` synapse (no receiver) self-delivers, landing in both journals.
- **Current behavior** (FACT): Each checkpoint writes a copy of the entire prior timeline into the journal; the next checkpoint includes the previous checkpoint's payload.
- **Why it matters** (INFERENCE): Repeated checkpoints (e.g. the rolling-update path, foundry closed loop) cause quadratic/exponential journal growth.
- **OS/product consequence**: Trusted-core checkpoint/rollback becomes a storage-amplification hazard.
- **Recommendation** (PROPOSAL): Persist checkpoints out-of-band via `CheckpointProtector` (already exists) instead of journaling a snapshot synapse.
- **Deletion/simplification**: yes.
- **Dependencies**: PERF-100, REL-102, and `CheckpointProtector.cs`.
- **Tests/measurements**: Take 3 sequential checkpoints; assert journal size does not multiply.
- **Effort**: M

### REL-102: `RestoreCheckpointAsync` appends without truncating
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `Neuron.cs:276-284` — loops `AddToJournal(ref _incomingSynapses, ...)` over the snapshot; no clear/replace.
- **Current behavior** (FACT): Restore adds the snapshot on top of current journal contents; state never shrinks and entries can duplicate.
- **Why it matters** (INFERENCE): A "rollback" grows rather than reverts the durable log; combined with REL-101 the rollback path is a growth source, and projections that count/dedupe may see doubled history.
- **OS/product consequence**: Rollback semantics are not true reverts.
- **Recommendation** (PROPOSAL): Define restore as replace-and-rebuild (truncate then seed), or move restore off the journal entirely.
- **Deletion/simplification**: yes.
- **Dependencies**: REL-101.
- **Tests/measurements**: Restore then read timeline; assert exact snapshot, no duplication/growth.
- **Effort**: M

### REL-103: Failed self-evolution apply is non-retriable and leaves partial side effects
- **Severity**: High | **Confidence**: High
- **Evidence**: `SelfEvolutionNeuron.cs:83-105` — `_decided.Add` before apply; on `Succeeded=false` no rollback fires unless `RollbackCheckpointId` is set (handlers never set it); a subsequent decision hits `_decided.Contains` → rejected.
- **Current behavior** (FACT): After a partial/failed apply, the proposal is terminally "decided" and cannot be re-applied; any side effects already fired persist.
- **Why it matters** (INFERENCE): Partial-write ambiguity with no recovery path in the governance rail itself.
- **OS/product consequence**: Governed mutations can wedge in a half-applied, unrecoverable state.
- **Recommendation** (PROPOSAL): Separate "decision recorded" from "apply result"; allow retry of a failed apply; require handlers to be idempotent or provide compensation/`RollbackCheckpointId`.
- **Deletion/simplification**: no.
- **Dependencies**: REL-104, SelfEvolutionApplyHandler.cs.
- **Tests/measurements**: Force a handler to throw mid-apply; assert either atomic no-op or a retriable/rolled-back state.
- **Effort**: M

### REL-104: `AutomationDefinitionApplyHandler` define is non-atomic
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `AutomationDefinitionApplyHandler.cs:30-39` fires `RegisterScript`, then `RegisterReaction`, then `CapabilityRegistered` sequentially.
- **Current behavior** (FACT): A failure between fires registers a script without its reaction (or without the capability record).
- **Why it matters** (INFERENCE): Combined with REL-103, the half-registered automation is permanent.
- **OS/product consequence**: Inconsistent automation state from an approved proposal.
- **Recommendation** (PROPOSAL): Make define idempotent-on-retry or fold into a single transactional synapse.
- **Deletion/simplification**: no.
- **Dependencies**: REL-103.
- **Tests/measurements**: Fault-inject between fires; assert consistency after retry.
- **Effort**: S

### REL-105: `PollTriggerNeuron` dedup is not durable
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `PollTriggerNeuron.cs:18` `_seen` in-memory HashSet; comment claims journal-enhanced but `EnsurePolls` (38-45) never rebuilds it.
- **Current behavior** (FACT): After reactivation `_seen` is empty; previously-seen poll items re-fire as new `trigger.poll.*` signals.
- **Why it matters** (INFERENCE): Duplicate automation triggers after any deactivation — a silent correctness/spam bug that contradicts its own comment.
- **OS/product consequence**: Non-idempotent trigger stream drives duplicate downstream effects.
- **Recommendation** (PROPOSAL): Persist the dedup cursor (journal or state) and rebuild on activation.
- **Deletion/simplification**: no.
- **Tests/measurements**: Reactivate; assert no re-fire for already-seen content.
- **Effort**: S

### REL-106: `ScheduleTriggerNeuron` ignores the cron schedule
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `ScheduleTriggerNeuron.cs:56-66` — fixed `FromSeconds(5)`/`FromMinutes(1)` reminder; `Schedule` expression stored but unused (comment admits it).
- **Current behavior** (FACT): Every scheduled reaction fires every ~1 minute regardless of its cron expression.
- **Why it matters** (INFERENCE): Scheduled automations do not run when the user specified; misleading capability.
- **OS/product consequence**: Wrong-time execution of user automations.
- **Recommendation** (PROPOSAL): Parse cron to compute next due time, or rename to a fixed-cadence trigger.
- **Deletion/simplification**: possibly delete cron field until implemented.
- **Tests/measurements**: Assert fire times match the expression.
- **Effort**: M

### FRAME-100: Journal-writer detection relies on a framework message substring
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `Neuron.cs:383-384` — `exception.GetBaseException().Message.Contains("state journal stream writer is not initialized", …)`.
- **Current behavior** (FACT): Fail-fast durability logic keys off Orleans' internal exception wording.
- **Why it matters** (INFERENCE): An Orleans preview update that rewords the message silently changes fail-fast into normal-throw (or vice versa).
- **OS/product consequence**: Brittle coupling in the trusted core's durability guard.
- **Recommendation** (PROPOSAL): Match on a typed exception or a stable code from the journaling API; confirm the supported detection against Orleans 10.2.1 journaling (currently undocumented — doc gap).
- **Deletion/simplification**: no.
- **Tests/measurements**: Simulate uninitialized-writer via the typed exception.
- **Effort**: M

### PERF-101: Reflection dispatch on every delivery for non-static-dispatch grains
- **Severity**: Low | **Confidence**: High
- **Evidence**: `Neuron.cs:331-367` — `GetInterfaces()` scan + `MethodInfo.Invoke` per `DeliverAsync` when static dispatch didn't handle.
- **Current behavior** (FACT): Reflection cost per message on the fallback path.
- **Why it matters** (INFERENCE): Hot-path allocation/CPU; the code even logs it as a "prototype" reliance.
- **Recommendation** (PROPOSAL): Cache resolved handler delegates per (type, synapse-type).
- **Deletion/simplification**: yes (cache).
- **Effort**: S

### PROD-100: `GeneratedNeuron` Gmail insights ships fabricated sample data as "analyzed locally"
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `GeneratedNeuron.cs:205-225,315-358` — `BuildGmailSampleRows(100)` synthetic rows presented via a "Gmail Insights … analyzed locally" surface.
- **Current behavior** (FACT): The experience renders invented emails as if analyzed.
- **Why it matters** (INFERENCE): A demo masquerades as a real capability in the trusted kernel.
- **OS/product consequence**: Misleading provider capability outside the connector model.
- **Recommendation** (PROPOSAL): Gate behind an explicit demo flag or route through the real Gmail connector.
- **Deletion/simplification**: yes.
- **Effort**: S

### PROD-101: `PerformKernelSelfUpdate` is a simulation presented as rolling-update capability
- **Severity**: Medium | **Confidence**: High
- **Evidence**: `SystemNeurons.cs:69-100` — hardcoded `replica 1..3`, `RestartResource` only logs, UI props assert `haReplicas:3`; `SystemRollingSurfaces.cs` hardcodes "/3".
- **Current behavior** (FACT): No real orchestration; emits UI surfaces describing a fake rollout, exercising the flawed checkpoint/restore path.
- **Why it matters** (INFERENCE): Aspirational behavior looks implemented; `KernelPack.Description` reinforces it.
- **OS/product consequence**: Overstated self-update capability in the trusted core.
- **Recommendation** (PROPOSAL): Quarantine as an explicit demo or implement real Aspire orchestration; keep the checkpoint path off the growth-prone journal (REL-101).
- **Deletion/simplification**: yes.
- **Dependencies**: REL-101/102, CLEAN-101.
- **Effort**: M

### CLEAN-100: Dead installed-pack path in `GeneratedNeuron`
- **Severity**: Low | **Confidence**: High
- **Evidence**: `GeneratedNeuron.cs:360-363` `LastInstalledPack()` returns `null`; the branch after `if (inst is null) return;` (167-203) is unreachable.
- **Recommendation**: delete the dead branch. **Deletion**: yes. **Effort**: S

### CLEAN-101: Test-only `FailAtReplica` field in a production handler
- **Severity**: Low | **Confidence**: High
- **Evidence**: `SystemNeurons.cs:86` `cmd.FailAtReplica == replica` forces a verify-failure.
- **Recommendation**: move failure injection to tests. **Deletion**: yes. **Effort**: S

### CLEAN-102: `WiringOptimizationProposed` is produced but never routed to the rail
- **Severity**: Low | **Confidence**: Medium
- **Evidence**: `SystemNeurons.cs:164` fires it; no consumer feeds it into `SelfEvolutionNeuron`.
- **Recommendation**: wire into the proposal rail or delete the optimizer's proposal path. **Deletion**: yes. **Effort**: S

### CLEAN-103: `JournalJson` polymorphism is shadowed by `EncryptedSynapseJsonConverter` in production
- **Severity**: Low | **Confidence**: Medium
- **Evidence**: `JournalJson.cs:24-37` sets `Synapse` polymorphism; hosting also adds `EncryptedSynapseJsonConverter` (a `JsonConverter<Synapse>`) to the same options, which takes precedence.
- **Recommendation**: document precedence; keep `JournalJson` for the non-encrypted path only, or remove the redundant config there. **Deletion**: partial. **Effort**: S

### CLEAN-104: `IKernelTask` is a dead contract
- **Severity**: Low | **Confidence**: High
- **Evidence**: `KernelTaskSynapses.cs:8`; no implementing grain or `GetGrain<IKernelTask>` caller in the repo (grep-confirmed).
- **Recommendation**: delete. **Deletion**: yes. **Effort**: S

### TEST-100: Self-evolution authorization and partial-apply recovery are untested
- **Severity**: Medium | **Confidence**: Medium
- **Evidence**: `tests/DigitalBrain.Tests/Kernel/SelfEvolution*Tests.cs` cover reject/approve/expire/duplicate/risk/rollback-required but supply a plain `DecidedBy` and never assert authorization or partial-apply recovery.
- **Why it matters** (INFERENCE): SEC-100/101 and REL-103 have no guarding tests, so regressions in the governance rail are invisible.
- **Recommendation** (PROPOSAL): Add tests for unauthorized decider rejection, `RequiresHumanApproval` enforcement, and retriable/atomic failed apply.
- **Effort**: M

---

## Second-pass corroborating audit (merged from redundant parallel audit `kernel-a.md`)

A redundant parallel audit independently reviewed the same files and is folded in here so all findings live in one subsystem document. Its findings use a different ID block; they are reconciled into the canonical findings register. Where it agrees with the primary audit above, treat as corroboration; where it adds new findings, they are additive.

## Findings

### SEC-050: Self-evolution approval identity (`DecidedBy`) is an unauthenticated free string
- **Severity**: Critical
- **Confidence**: High
- **Evidence**: `SelfEvolution/SelfEvolutionNeuron.cs:54-60` — the only check on the approver is `if (string.IsNullOrWhiteSpace(decision.DecidedBy))`. `SelfEvolution.cs:82-86` defines `DecidedBy` as a plain `string`. Approved decisions then call `_applyRegistry.ApplyAsync` (92).
- **Current behavior** (FACT): any actor able to deliver a `SelfEvolutionDecision` to `self-evolution-main` (point-to-point `DeliverAsync`, broadcast, MCP, or another neuron) approves any pending proposal by supplying an arbitrary non-empty `DecidedBy` (e.g. `"user:owner"`). No principal, session, grant, or signature is checked.
- **Why it matters** (INFERENCE): the entire self-evolution vision rests on "only after `SelfEvolutionDecision.Approved` does an apply handler run". If approval is a string anyone can type, the approval is decorative. Contrast the INO path, which binds approvals to `decidedBy` inside revision-guarded conversation state and signed effect plans.
- **OS/product consequence**: breaks the governed self-evolution rail — the primary trust boundary for prompt/automation/generated-code mutations.
- **Recommendation** (PROPOSAL): require an authenticated principal + grant for decisions (mirror `InoEffectPlanAuthority` signed tokens, or gate the decision behind the same session/grant model the INO approval path uses). Reject decisions whose approver is not an authorized human principal for `proposal.Scope`.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-051, SEC-052, SEC-056; Foundry (proposer), MCP surface.
- **Tests/measurements required**: test that a decision from an unauthorized/anonymous principal is rejected and no apply handler runs.
- **Effort**: L
- **Migration/rollback concern**: changes the rail contract; existing bootstrap flows that self-approve must move to a trusted path.

### SEC-051: `RequiresHumanApproval` is set but never enforced
- **Severity**: High
- **Confidence**: High
- **Evidence**: `SelfEvolution/SelfEvolutionNeuron.cs` never references `RequiresHumanApproval`. Grep confirms the only read/write of the field is the producer `Foundry/CodeFoundryClosedLoopNeuron.cs:72` (`RequiresHumanApproval: true`); the neuron ignores it.
- **Current behavior** (FACT): a proposal's own request for human approval has no effect on how the decision is processed; there is no distinction between machine-approved and human-approved decisions.
- **Why it matters** (INFERENCE): combined with SEC-050, there is no mechanism that guarantees a *human* approved anything, even when the proposal explicitly demands it.
- **OS/product consequence**: the "human-approved" guarantee in the North-Star vision is not implemented for the neuron rail.
- **Recommendation** (PROPOSAL): when `RequiresHumanApproval` is true, require a decision carrying proof of human assurance (assurance level / interactive session), not a bare string; otherwise reject.
- **Deletion/simplification opportunity**: no (the field should become load-bearing, not deleted).
- **Dependencies**: SEC-050.
- **Tests/measurements required**: proposal with `RequiresHumanApproval:true` cannot be applied by a system-principal decision.
- **Effort**: M
- **Migration/rollback concern**: none beyond SEC-050.

### SEC-052: Apply-risk gate trusts the proposer-supplied `Risk`
- **Severity**: High
- **Confidence**: High
- **Evidence**: `SelfEvolution/SelfEvolutionApplyHandler.cs:39-42` gates `if (proposal.Risk > handler.MaxRisk)`. `Risk` is a field on the proposer-constructed `SelfEvolutionProposal` (`SelfEvolution.cs:31`).
- **Current behavior** (FACT): the disruptive-blast-radius gate compares the handler's `MaxRisk` against a value the proposer chose. A proposer that under-declares `Risk` (e.g. `None`) passes any handler's ceiling.
- **Why it matters** (INFERENCE): the risk ceiling is meant to stop a high-blast-radius change from running through a low-risk handler, but the input is attacker/error-controlled.
- **OS/product consequence**: weakens the risk-tiering safety control on the apply path.
- **Recommendation** (PROPOSAL): derive the effective risk from `ApplyVia` (the handler already knows its tier) and ignore/verify the proposal's declared `Risk` against it; reject on mismatch.
- **Deletion/simplification opportunity**: yes — the proposal `Risk` field can become advisory-only.
- **Dependencies**: SEC-050.
- **Tests/measurements required**: a `FoundryDeploy` proposal declaring `Risk:None` is still gated as `KernelRestart`.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-053: Foundry `TrustedAutoApply` is a config-gated bypass of the rail
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `Foundry/CodeFoundryClosedLoopNeuron.cs:29-40, 119-120` — with `request.AutoApply` and `DigitalBrain:Foundry:TrustedAutoApply=true`, it fires `AuditBypass(...)` and calls `ApplyImmediatelyAsync` (runs/deploys generated code) **without** staging a `SelfEvolutionProposal` or awaiting any decision. (File is in the Foundry subsystem but is the direct producer/bypass of this rail.)
- **Current behavior** (FACT): generated code executes or builds+restarts with no proposal/decision when a config flag is set; the only record is an `AuditBypass` synapse.
- **Why it matters** (INFERENCE): this is an explicit, trusted, config-gated bypass — acceptable by the CLAUDE.md doctrine *only if* the config is genuinely trusted and never reachable by a user/MCP path. It is worth listing because it is the one sanctioned hole in the "only path is the rail" claim.
- **OS/product consequence**: if `TrustedAutoApply` is ever enabled in an environment reachable by untrusted input, generated code self-applies.
- **Recommendation** (PROPOSAL): keep the bypass explicit and default-off (it is), but ensure it is never settable per-tenant/per-request and that `AutoApply` from any user/MCP-originated `FoundryRequest` is forced false.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-050.
- **Tests/measurements required**: user-originated foundry request cannot auto-apply even with the flag on.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-054: `AutomationNeuron.DefineReactionAsync` bypasses the approval rail
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `Grains/AutomationNeuron.cs:277-285` fires `RegisterScript` + `RegisterReaction` directly. Its own comment (280-281) says public entry points must stage a `SelfEvolutionProposal` first and that "direct calls bypass the approval rail and are only for trusted bootstrap or internal apply handlers."
- **Current behavior** (FACT): this method (and the raw `RegisterScript`/`RegisterReaction`/`AutomationApp` synapses handled in `DispatchSynapse`) register executable automation with no proposal/decision.
- **Why it matters** (INFERENCE): the safety of the automation subsystem depends entirely on nothing user/MCP-facing reaching these entry points. That invariant is asserted in a comment, not enforced in code.
- **OS/product consequence**: a second path (besides SEC-053) by which executable behaviour can be added without the rail.
- **Recommendation** (PROPOSAL): make `DefineReactionAsync` internal to trusted bootstrap/apply-handler assemblies, and verify no MCP tool or Ino path fires bare `RegisterScript`/`RegisterReaction` at an `AutomationNeuron`.
- **Deletion/simplification opportunity**: possibly (fold into the apply handler only).
- **Dependencies**: SEC-050, AutomationDefinitionApplyHandler.
- **Tests/measurements required**: architecture test that only apply handlers/bootstrap reference `DefineReactionAsync` / emit `RegisterReaction`.
- **Effort**: M
- **Migration/rollback concern**: none.

### SEC-055: `GeneratedNeuron` executes journal-sourced pack code with only a try/catch
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `Grains/GeneratedNeuron.cs:88-127` — `EnsureEmbodied` builds a `GeneratedPackRuntime` from `OutgoingJournal.Concat(IncomingJournal)` and `TryDispatchEmbodiedAsync` calls `embodied.Handle(synapse)` (105), broadcasting the outputs.
- **Current behavior** (FACT): a neuron embodies a pack from journal contents and runs its `Handle`/`Respond` code for every matching broadcast; failures are caught and reported as `PackEmission("pack-error:...")`.
- **Why it matters** (INFERENCE): if a pack can be embodied without a completed approved proposal for *that pack*, this is an execution surface for self-modification. The linkage between "approved SelfEvolutionProposal" and "what ends up in this neuron's journal as an embodiable pack" is not visible in this file (it depends on the Foundry deploy handler + pack contracts, out of this file list).
- **OS/product consequence**: the durable-code-execution surface of the self-evolution product; needs an auditable approve→embody chain.
- **Recommendation** (PROPOSAL): confirm (and test) that a pack can only be embodied via an applied, approved proposal, and that the embodied source is content-addressed/signed. Flag for cross-subsystem review with the Foundry/Pack audit.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-050, Foundry deploy handler, Pack contracts.
- **Tests/measurements required**: embodiment refused without a matching applied proposal.
- **Effort**: L
- **Migration/rollback concern**: none.

### SEC-056: No tenant/principal boundary inside the neuron self-evolution + automation rail
- **Severity**: High
- **Confidence**: High
- **Evidence**: `SelfEvolution/SelfEvolutionNeuron.cs` (whole) and `Grains/AutomationNeuron.cs` (whole) carry no tenant/workspace/principal concept. `SelfEvolutionNeuronIds.Main = "self-evolution-main"` (`SelfEvolution.cs:37-40`) is a single global grain. Automation `ScopeMatches` (`AutomationNeuron.cs:191-209`) is a loose string prefix match that defaults to *allow* ("default loose for compat", 208).
- **Current behavior** (FACT): the rail is a single global queue with no tenant isolation; automation scope matching fails open.
- **Why it matters** (INFERENCE): the audit standard requires "tenant-isolated ... fail-closed at every authorization/mutation boundary." The INO runtime meets this (actorScope, surface-feed identity checks); the neuron rail does not.
- **OS/product consequence**: cross-tenant self-evolution/automation contamination; no per-tenant approval authority.
- **Recommendation** (PROPOSAL): scope the self-evolution grain per tenant/workspace, make automation `ScopeMatches` fail closed, and require the decision principal to own `proposal.Scope`.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-050, ARCH-053.
- **Tests/measurements required**: tenant A cannot approve/observe tenant B's proposals; scope mismatch does not execute.
- **Effort**: L
- **Migration/rollback concern**: grain-key change → journal migration for existing `self-evolution-main`.

### ARCH-050: `PerformKernelSelfUpdate` rolling update is simulated, not real
- **Severity**: High
- **Confidence**: High
- **Evidence**: `Grains/SystemNeurons.cs:69-100` loops `replica = 1..3` (hardcoded), emits drain/verify/rollback/complete UiSurfaces (`SystemRollingSurfaces.cs`) and a `RestartResource` synapse. The `RestartResource` handler (38-67) only logs and emits surfaces. "Verification" is `GetCausalLineageAsync(...).Count` (journal row count). Rollback is `RestoreCheckpointAsync` (see ARCH-051).
- **Current behavior** (FACT): no resource is drained, restarted, health-checked, or version-swapped. `haReplicas`/`replicasProcessed` are literal `3`. The only "abort" path is driven by the test-only `FailAtReplica` input.
- **Why it matters** (INFERENCE): presented (via `KernelPack.Description = "Core kernel substrate with rolling replica support."` and the surfaces) as a real rolling-update capability; it is a UI/journal simulation.
- **OS/product consequence**: misrepresents a core "self-evolving OS" capability; a caller could believe the kernel safely self-updated when nothing happened.
- **Recommendation** (PROPOSAL): either implement against real Aspire/orchestrator resource commands (drain, restart, health-gate) or clearly mark as a demo and remove the "rolling replica support" claim.
- **Deletion/simplification opportunity**: yes — delete or quarantine as demo.
- **Dependencies**: ARCH-051, SystemRollingSurfaces.
- **Tests/measurements required**: real integration test that a replica restart + state continuity actually occurs.
- **Effort**: L
- **Migration/rollback concern**: none (nothing real to roll back today).

### ARCH-051: Checkpoint/restore is an additive journal snapshot, not a state restore
- **Severity**: High
- **Confidence**: High
- **Evidence**: `Neuron.cs:247-256` (`CreateCheckpointAsync` snapshots both journals and *fires the Checkpoint into the outgoing journal*), `276-284` (`RestoreCheckpointAsync` appends the snapshot synapses back into the *incoming* journal, no clear, no re-dispatch).
- **Current behavior** (FACT): "restore" does not revert the neuron to the checkpointed state — it *appends* the checkpointed synapses to the current (already-advanced) incoming journal. Projections rebuilt afterward see both the old and the appended-again events. There is no removal of post-checkpoint events.
- **Why it matters** (INFERENCE): a real rollback must discard changes made after the checkpoint. This implementation grows the journal and can double-count events (e.g. duplicate `RegisterReaction` on replay), and the "restored" projection is not the checkpointed projection. Survives a silo crash only in the sense that the journal is durable — but the semantics are wrong either way.
- **OS/product consequence**: the "rollback-capable" guarantee (used by self-evolution rollback and the simulated kernel update) is not met.
- **Recommendation** (PROPOSAL): implement restore as either (a) event-log truncation to the checkpoint boundary, or (b) a durable state snapshot that replaces current state, with idempotent replay. Define whether restore should re-dispatch.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-050 (uses it for "rollback"), REL-050 (unbounded growth compounds this).
- **Tests/measurements required**: after restore, projection equals the checkpoint projection and post-checkpoint effects are gone; no duplicate reactions after restore+replay.
- **Effort**: L
- **Migration/rollback concern**: journal-format implications if truncation is chosen.

### ARCH-052: MetaOptimizer emits LLM-generated `WiringOptimizationProposed` that dead-ends (latent injection vector)
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `Grains/SystemNeurons.cs:149-165` builds an LLM prompt from telemetry counts and fires `WiringOptimizationProposed`; the handler `168-172` only logs. Grep confirms no other consumer and no bridge to `SelfEvolutionProposal`.
- **Current behavior** (FACT): LLM output is logged, not applied. It does **not** currently enter any apply path.
- **Why it matters** (INFERENCE): today this is harmless, but it is a telemetry→LLM→"proposal" pipeline; if a future change routes `WiringOptimizationProposed` into the self-evolution rail, telemetry-borne prompt injection could drive self-modification.
- **OS/product consequence**: none today; a documented hazard for the rail.
- **Recommendation** (PROPOSAL): if this is speculative, delete it (Elon step 2); if kept, document that its output must never auto-enter the apply path without human approval.
- **Deletion/simplification opportunity**: yes — strong delete candidate.
- **Dependencies**: SEC-050.
- **Tests/measurements required**: n/a unless wired.
- **Effort**: S
- **Migration/rollback concern**: none.

### ARCH-053: `self-evolution-main` singleton assumption is unenforced across multi-key/multi-silo
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `SelfEvolution.cs:37-40` (`Main` constant); `SelfEvolutionNeuron` keeps `_pending/_decided/_applied/_expired` in-memory dictionaries rebuilt from the journal on activate (`RebuildProjection`, 108-151). Foundry addresses it by the fixed key (`CodeFoundryClosedLoopNeuron.cs:62`).
- **Current behavior** (FACT): correctness relies on Orleans' single-activation-per-key guarantee for the one `Main` key; there is no per-tenant keying (SEC-056) and no defense if callers use other keys. Because projections are rebuilt from the durable journal, a reactivation is consistent — but the design assumes exactly one logical queue.
- **Why it matters** (INFERENCE): a single global approval queue is a scaling/isolation bottleneck and conflates tenants; any accidental second key silently creates an independent, unaudited queue.
- **OS/product consequence**: shared authority across tenants; hard to reason about "who can approve what".
- **Recommendation** (PROPOSAL): key the rail per scope and document the single-activation reliance; add an architecture test that only `SelfEvolutionNeuronIds.Main` (or scoped ids) are used.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-056.
- **Tests/measurements required**: concurrent proposals on the same key serialize; scoped keys isolate.
- **Effort**: M
- **Migration/rollback concern**: journal migration if re-keyed.

### REL-050: Neuron journals grow unboundedly — no compaction, truncation, or archival
- **Severity**: High
- **Confidence**: High
- **Evidence**: `Neuron.cs` — `FireAsync` (199) and `DeliverAsync` (290) `Add` to `IDurableList<Synapse>` and never remove. There is no compaction/snapshot/trim anywhere in the base or any `Grains/*` file. `CreateCheckpointAsync` even *adds* another synapse (254). Contrast the INO runtime, which uses bounded `EncryptedPersistentState` with `MaximumCiphertextBytes = 4MB` and archival segments.
- **Current behavior** (FACT): every synapse a neuron sends or receives is durably retained forever. Long-lived neurons (e.g. `self-evolution-main`, `AutomationNeuron`, `ObservabilityNeuron`, poll/schedule triggers) accumulate without bound.
- **Why it matters** (INFERENCE): unbounded storage growth; unbounded activation cost because every activation replays the full list (REL-053); eventual failure against any per-grain journal/blob size ceiling. This is the substrate the trust rail is built on.
- **OS/product consequence**: durability substrate becomes a liability — the "durable, replayable" property degrades to "grows until it breaks".
- **Recommendation** (PROPOSAL): add journal compaction/snapshotting (fold to a projection + prune, or periodic state snapshot with event trim), or migrate long-lived neurons to the bounded `EncryptedPersistentState` model used by the INO runtime.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-053, ARCH-051, FRAME-050.
- **Tests/measurements required**: journal size stays bounded under sustained traffic; activation time does not grow with age.
- **Effort**: L
- **Migration/rollback concern**: compaction must preserve replay determinism.

### REL-051: Approved proposal can be recorded but never applied after a crash (no retry)
- **Severity**: High
- **Confidence**: High
- **Evidence**: `SelfEvolution/SelfEvolutionNeuron.cs:83-97` fires the durable `SelfEvolutionDecisionRecorded` (85) *before* calling `ApplyAsync` and firing `SelfEvolutionApplyResult` (92-96). `RebuildProjection:140-150` marks `_decided` from `DecisionRecorded` and `_applied` only from a *successful* `ApplyResult`.
- **Current behavior** (FACT): if the silo dies between the `DecisionRecorded` write and the `ApplyResult` write, on reactivation the proposal is `_decided`, removed from `_pending`, and not `_applied`. Re-delivering the decision hits "already been decided" (62-66). The approved effect is silently lost with no retry.
- **Why it matters** (INFERENCE): approved changes must be applied exactly once and be recoverable. This is an approval→apply atomicity gap.
- **OS/product consequence**: violates "durable, replayable, rollback-capable" for the rail — an approved self-evolution can vanish.
- **Recommendation** (PROPOSAL): make apply idempotent and driven off durable state (e.g. on activation, re-drive any `Decided && Approved && !Applied` proposal through the apply registry), or use a two-phase "decision recorded → apply pending → applied" projection with retry.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-052, SEC-050.
- **Tests/measurements required**: crash-injection between DecisionRecorded and ApplyResult still applies exactly once on reactivation.
- **Effort**: M
- **Migration/rollback concern**: none.

### REL-052: Transient apply failure permanently blocks the proposal
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `SelfEvolution/SelfEvolutionNeuron.cs:83-106` — on `Approved`, the proposal is moved to `_decided` (84) before apply; a `Failed` `SelfEvolutionApplyResult` leaves it `_decided` but not `_applied`. Re-sending the decision is rejected as "already been decided" (62-66). Only a `RollbackCheckpointId` produces a `SelfEvolutionRollbackRequired` (98-105) — there is no re-apply path.
- **Current behavior** (FACT): a transient apply exception (e.g. grain unavailable) burns the proposal; it can never be retried or re-approved.
- **Why it matters** (INFERENCE): no distinction between "rejected by human" and "apply failed transiently"; the operator must recreate a fresh proposal.
- **OS/product consequence**: brittle rail; reduces recoverability.
- **Recommendation** (PROPOSAL): distinguish decision-recorded from apply-succeeded; allow re-apply of a decided-but-unapplied proposal (idempotently) instead of treating any decided proposal as terminal.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-051.
- **Tests/measurements required**: transient apply failure is retryable and eventually applies once.
- **Effort**: M
- **Migration/rollback concern**: none.

### REL-053: Full-journal replay on every activation / projection rebuild
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `SelfEvolutionNeuron.RebuildProjection:115` (`IncomingJournal.Concat(OutgoingJournal).ToArray()` then multiple `OfType<>` passes); `AutomationNeuron.EnsureProjections:219-258` (multiple full `Concat` scans); `PollTriggerNeuron.EnsurePolls:40-45`; `ScheduleTriggerNeuron.EnsureScheduled:44-54`; `ObservabilityNeuron.PublishGraphFromJournalAsync:124-130`.
- **Current behavior** (FACT): each activation (and several per-message calls) linearly re-scans the entire durable journal to rebuild in-memory projections.
- **Why it matters** (INFERENCE): with REL-050 (unbounded growth), activation cost and per-message cost grow with grain age — a latency and CPU cliff for long-lived neurons.
- **OS/product consequence**: degrading responsiveness of core substrate neurons over time.
- **Recommendation** (PROPOSAL): snapshot projections durably and replay only the tail since the last snapshot; cache projections instead of rebuilding on `Ensure*` per message.
- **Deletion/simplification opportunity**: yes (fewer redundant scans).
- **Dependencies**: REL-050.
- **Tests/measurements required**: activation time constant vs journal length.
- **Effort**: M
- **Migration/rollback concern**: none.

### FRAME-050: Trust substrate built on alpha Orleans Journaling (`10.2.1-preview.1.alpha.1`, ORLEANSEXP005)
- **Severity**: High
- **Confidence**: High
- **Evidence**: `DigitalBrain.Kernel.csproj:46` references `Microsoft.Orleans.Journaling`; `:10-12` `NoWarn ORLEANSEXP005`. `Neuron.cs:7,10` `using Orleans.Journaling; #pragma warning disable ORLEANSEXP005`. `DurableGrain` + `IDurableList<Synapse>` + `WriteStateAsync` are the persistence primitives for every neuron and the self-evolution rail.
- **Documentation gap** (FACT): Context7 lookup for the pinned version failed (monthly quota exceeded). Microsoft Learn returns docs for the *stable* `JournaledGrain`/log-consistency event-sourcing API (`Orleans.EventSourcing`), **not** the newer experimental `Orleans.Journaling` `DurableGrain`/`IDurableList` API this code uses. Microsoft Learn has no version-specific contract page for `10.2.1-preview.1.alpha.1` in the returned results. I could not verify the alpha API's durability/compaction guarantees against first-party docs; recorded as a documentation gap rather than invented.
- **Current behavior** (FACT): the entire neuron durability, causation, checkpoint, and self-evolution-rail trust model depends on an alpha-tagged, experimentally-flagged API whose contract is not documented in accessible first-party sources and whose semantics (e.g. whether `IDurableList` compacts) are unverified here.
- **Why it matters** (INFERENCE): building "the only path for user-visible mutations" on an alpha API risks breaking changes, undocumented failure modes, and (per REL-050) no built-in compaction. Alpha APIs can change signature or semantics between previews.
- **OS/product consequence**: the durable/replayable/rollback substrate — the OS's foundation — has an unstable, under-documented dependency.
- **Recommendation** (PROPOSAL): (1) obtain and pin the exact API contract (with a Context7 key or the package's own docs/source) and record compaction/retention semantics; (2) treat the alpha dependency as a risk with an exit plan (abstraction seam so the substrate can move to a stable persistence model); (3) isolate the rail's persistence behind an interface so an API break does not ripple through every neuron.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-050, REL-053, ARCH-051.
- **Tests/measurements required**: pinned-version durability contract test; upgrade smoke test on preview bumps.
- **Effort**: L
- **Migration/rollback concern**: substrate migration is high-blast-radius.

### PERF-050: Repeated `Concat` + `ToArray` snapshots of full journals per query
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `Neuron.cs:223-238` (`GetTimelineAsync`/`GetCausalLineageAsync` do `SnapshotTimeline(...).ToArray()` with `Concat`+`Where`+`OrderBy`+`DistinctBy`), `252` (`CreateCheckpointAsync` `Concat().DistinctBy().ToList()`).
- **Current behavior** (FACT): every timeline/lineage/checkpoint query allocates a full materialised copy of the journal(s).
- **Why it matters** (INFERENCE): allocation and CPU scale with journal size (compounded by REL-050); frequent MCP/UI lineage queries repeatedly copy large lists.
- **OS/product consequence**: diagnostic/UI queries get slower as neurons age.
- **Recommendation** (PROPOSAL): maintain indexed projections (by correlation id) rather than re-scanning; avoid full-copy snapshots for read-only callers.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: REL-050, REL-053.
- **Tests/measurements required**: allocation profile constant vs journal length.
- **Effort**: M
- **Migration/rollback concern**: none.

### PERF-051: Reflection assembly scan for synapse types at silo start
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `Kernel/JournalJson.cs:41-55` and `Runtime/EncryptedSynapseJsonConverter.cs:46-53` each enumerate all loaded `DigitalBrain.*` assemblies and reflect over `GetTypes()` to discover `Synapse` subtypes.
- **Current behavior** (FACT): a full reflection type scan runs when the journal JSON options and encrypted converter are configured at startup.
- **Why it matters** (INFERENCE): one-time cost, but reflection over all types is brittle if assemblies load lazily (a synapse type not yet loaded is silently absent → `FailSerialization` at write time, which is at least fail-closed).
- **OS/product consequence**: minor startup cost; potential allow-list gaps if assembly loading is lazy.
- **Recommendation** (PROPOSAL): consider source-generated type lists; ensure eager load of all synapse-bearing assemblies before building the resolver (JournalJson already calls `LoadReferencedDigitalBrainAssemblies`).
- **Deletion/simplification opportunity**: minor.
- **Dependencies**: none.
- **Tests/measurements required**: all concrete synapse types are present in both allow-lists.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-050: Dead code in `GeneratedNeuron` (`EmitConfigFormIfRequiredAsync`, `LastInstalledPack`)
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `Grains/GeneratedNeuron.cs:69-86` `EmitConfigFormIfRequiredAsync` is never called (grep). `360-363` `LastInstalledPack()` always returns `null`, making the entire `inst is null` / LLM-embodiment branch in `UseExperienceAsync` (167-203) effectively dead except the null path.
- **Current behavior** (FACT): unreachable/no-op code paths.
- **Why it matters** (INFERENCE): misleads readers into thinking config forms and installed-pack LLM behaviour are wired; increases audit surface.
- **OS/product consequence**: none functional; maintainability debt.
- **Recommendation** (PROPOSAL): delete `EmitConfigFormIfRequiredAsync` and the dead `LastInstalledPack` branch, or implement them.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: none.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-051: Empty `catch { }` swallows pack-config-store faults
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `Grains/LlmResponderNeuron.cs:55-59` — catches `OperationCanceledException` then a bare `catch { /* config optional */ }`.
- **Current behavior** (FACT): any store failure (not just "no config") is silently ignored, falling back to ask-specific/global client.
- **Why it matters** (INFERENCE): a genuinely broken config store (auth, corruption) is indistinguishable from "no override configured".
- **OS/product consequence**: silent degradation of user-selected model routing.
- **Recommendation** (PROPOSAL): log at debug/warning inside the catch; keep the fallback.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: store fault is logged.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-052: `ScheduleTriggerNeuron` ignores the cron `Schedule`; fixes 1-minute period
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `Grains/ScheduleTriggerNeuron.cs:56-66` registers `RegisterOrUpdateReminder(id, 5s, 1min)` and the comment (60-61) says the `Schedule` cron expression is stored "for future use / UI" but not parsed.
- **Current behavior** (FACT): every scheduled reaction fires each minute regardless of its declared cron.
- **Why it matters** (INFERENCE): the `Schedule` field is misleading — users defining `"0 9 * * *"` still get per-minute firing.
- **OS/product consequence**: incorrect automation timing.
- **Recommendation** (PROPOSAL): parse cron and compute due time, or remove the field until implemented.
- **Deletion/simplification opportunity**: yes (remove misleading field) or implement.
- **Dependencies**: none.
- **Tests/measurements required**: reaction fires per its cron.
- **Effort**: M
- **Migration/rollback concern**: none.

### CLEAN-053: Dockerfile copies whole context on a preview base
- **Severity**: Note
- **Confidence**: Medium
- **Evidence**: `Dockerfile:14` `COPY . .`; base images `mcr.microsoft.com/dotnet/sdk:11.0-preview` / `aspnet:11.0-preview`.
- **Current behavior** (FACT): whole-repo build context (no `.dockerignore` referenced) and a preview runtime base.
- **Why it matters** (INFERENCE): large/ slow builds; preview base is not production-grade.
- **OS/product consequence**: deployment maturity.
- **Recommendation** (PROPOSAL): add `.dockerignore`, pin a stable base for production.
- **Deletion/simplification opportunity**: minor.
- **Dependencies**: none.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

---

## Answers to subsystem-specific questions

**1. Neuron base + NeuronJournals — lifecycle, journal bounds, replay, dispatch; alpha journaling risk; compaction.**
Lifecycle: `OnActivateAsync` resolves the two keyed `IDurableList<Synapse>` (`in-journal`/`out-journal`), fails fast if unregistered, optionally journals an activation marker, and subscribes to the broadcast timeline iff the neuron declares `IHandle<T>` (or overrides `ShouldSubscribeToTimeline`). Dispatch/routing: `FireAsync` stamps causation (`_currentCause`), appends to the outgoing durable list, `WriteStateAsync`, then routes broadcast→stream / `Receiver`→`DeliverAsync` / else self-deliver. `DeliverAsync` appends to the incoming list, writes, sets `_currentCause` (nested via plain field + finally because Orleans grains are non-reentrant), and dispatches via declared `IHandle<T>` or a reflection fallback. Replay: there is no framework "replay handlers" step — neurons rebuild in-memory **projections** by scanning the full journals on activation (`RebuildProjection`, `EnsureProjections`, etc.). **Journals are unbounded** — nothing compacts, truncates, or archives them (REL-050); every query/checkpoint re-materialises them (REL-053, PERF-050). Alpha risk: the whole substrate uses the ORLEANSEXP005-flagged alpha `Orleans.Journaling` API; I could not verify its durability/compaction contract against first-party docs (Context7 quota exhausted; Microsoft Learn only documents the stable `JournaledGrain` event-sourcing API, not `DurableGrain`/`IDurableList`) — recorded as FRAME-050 documentation gap + risk. **Building the trust substrate on this alpha API is a real risk**: unstable contract, undocumented retention, and no compaction.

**2. Self-evolution rail — full trace + every bypass; is DecidedBy authenticated?**
Trace: proposer (`CodeFoundryClosedLoopNeuron.StageApplyAsync`) `DeliverAsync`es a `SelfEvolutionProposal` to `self-evolution-main` → `HandleAsync(proposal)` validates (`ProposalId/ApplyVia/Origin/RollbackPlan` required, expiry, dedup) and fires `SelfEvolutionProposalPending` → a `SelfEvolutionDecision` arrives → `HandleAsync(decision)` checks only non-empty `DecidedBy`, that a matching pending exists, and not expired, then fires `SelfEvolutionDecisionRecorded`; if `Approved`, calls `SelfEvolutionApplyRegistry.ApplyAsync` (handler dispatch + `proposal.Risk > handler.MaxRisk` gate) → fires `SelfEvolutionApplyResult`; on failure with a checkpoint id, fires `SelfEvolutionRollbackRequired`. **`DecidedBy` is NOT authenticated** — it is a free string gated only by `IsNullOrWhiteSpace` (SEC-050). **Every place an effect can run without a recorded *authenticated, human* approval**: (a) SEC-050 — approval is a spoofable string; (b) SEC-051 — `RequiresHumanApproval` is never enforced; (c) SEC-052 — the risk gate trusts proposer-supplied `Risk`; (d) SEC-053 — Foundry `TrustedAutoApply` runs generated code with no proposal (only an `AuditBypass` synapse); (e) SEC-054 — `AutomationNeuron.DefineReactionAsync` / raw `RegisterScript`/`RegisterReaction` register executable automation with no rail; (f) SEC-055 — `GeneratedNeuron` executes journal-sourced pack code; the approve→embody linkage is not visible in this subsystem. There is **no** verify step in the rail (the `RollbackPlan` is a string; verification is not executed), and rollback is only *requested* (a synapse), not performed.

**3. `PerformKernelSelfUpdate` — real or theatre?**
Theatre. Implemented: emitting a sequence of drain/verify/rollback/complete `UiSurface`s and `RestartResource`/`SystemStatusChanged` synapses; taking a `Checkpoint` synapse; an abort path driven by the test-only `FailAtReplica`. Placeholder: replica count is hardcoded `1..3`/`haReplicas:3`/`replicasProcessed:3`; "verification" is a journal row count; the `RestartResource` handler only logs. Aspirational: actual draining, resource restart, health-gated verification, real rollback of state. Nothing restarts a resource or restores real state (ARCH-050, ARCH-051).

**4. CreateCheckpoint/RestoreCheckpoint/GetCausalLineage — what persists/restores; survive crash/new activation?**
`CreateCheckpointAsync` snapshots a de-duplicated union of both journals into a `Checkpoint` record and **fires it into the outgoing journal** (so it is durable *as an event*, and grows the journal). `RestoreCheckpointAsync` **appends** the checkpoint's snapshot back into the *incoming* journal without clearing current state or re-dispatching — so it is not a true restore (ARCH-051): post-checkpoint events remain, and replayed events can double-count. `GetCausalLineageAsync` filters both journals by `CorrelationId`/`SynapseId`, orders by timestamp, de-dups. Crash survival: because the journals are durable, a snapshot fired before a crash survives; but "restore" would produce a *superset* journal (old + re-appended), and a new activation rebuilds projections from that superset — i.e. it survives but with wrong semantics. The separate `CheckpointProtector`/`AddKernelSecurity` path can AES-GCM-encrypt a `Checkpoint` for at-rest protection, but does not fix the additive-restore semantics.

**5. Grain keying and single-activation assumptions.**
`SelfEvolutionNeuron` assumes a single global `self-evolution-main` activation (`SelfEvolutionNeuronIds.Main`); its in-memory projection dictionaries are rebuilt from the durable journal on activate, so a normal single-activation reactivation is consistent, but there is no per-tenant keying (SEC-056) and any accidental alternate key creates an independent, unaudited queue (ARCH-053). Automation/poll/schedule/observability neurons are keyed by whatever the caller passes (`"automation-main"` in tests) and hold in-memory projections (some, like `PollTriggerNeuron._seen`, are **not** rebuilt from journal, so dedup is lost across reactivations). Orleans' single-activation-per-key guarantee is relied on but not defended; multi-silo is fine for a single key, but there is no cross-tenant isolation and no protection against multiple logical queues.

**6. MetaOptimizer LLM `WiringOptimizationProposed` — does it enter an apply path?**
No. `MetaOptimizerNeuron` fires LLM-generated `WiringOptimizationProposed`, and the only consumer is its own handler which just logs (ARCH-052). It does **not** bridge to `SelfEvolutionProposal` or any apply handler today. It is therefore not currently a self-modification vector — but it is a live telemetry→LLM→"proposal" pipeline that would become a prompt-injection→self-modification vector if ever wired into the rail. Recommend deleting it or explicitly documenting the prohibition.

**7. Where are identity/principal/tenant boundaries enforced inside the kernel runtime?**
Only in the **INO runtime** half (`Runtime/*`), not the neuron/self-evolution half. Enforcement points: `AgentFrameworkWorkflowRunner` requires a valid 64-hex `ActorScope` and derives `RequestScope.Id(tenant, workspace, principal)`; `EncryptedRuntimeStateProtector` binds tenant/kind/schema/revision into AAD and signs it; `InoConversationOutboxDispatcherGrain`/`EnsureFeedAsync` throws `UnauthorizedAccessException` on surface-feed identity mismatch; `InoEffectPlanAuthority` HMAC-binds plan/execution to `actorScope`+tool; `InoEffectPlanNeuron`/`PlanInoToolGateway` validate the full binding before any provider call; `ClosedInoToolGateway` fails closed by default; `ConversationNeuron.DecideApprovalWithAssistantAsync` records an authenticated `decidedBy` inside revision-guarded state. By contrast, the neuron self-evolution + automation rail (`SelfEvolutionNeuron`, `AutomationNeuron`) has **no** tenant/principal boundary (SEC-050, SEC-056) — the two halves of this subsystem do not share a trust model, and the governed rail is on the weaker one.
