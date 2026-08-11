# S1.4-GRILL — Execution kernel seam

**Subject:** `d2d66e64` S1.4-GREEN Tasks → Execution rename+deepen  
**Role:** GRILL (judge only; no code or git changes)  
**Green report:** `plans/stage1/reports/S14-execution-green.md`  
**Brief:** `plans/stage1/briefs/S14-execution-green.md`

---

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  (AppHost node NO_COLOR/FORCE_COLOR noise only)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 131, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~46s
```

ChartVocabularyProofs did not flake this run. Suite is green.

---

## Attack matrix

### (1) Ratified conformance — external surface, vocabulary, MAF

| Check | Result |
|-------|--------|
| Hybrid surface on `IExecution` | **PASS** — only `Apply(ApplyExecution)` + `Read()`; `[ClientEntryPoint]`; aliases `db.execution` / method names |
| Cancel as Apply command | **PASS** — `CancelExecution : ExecutionApplyCommand`; requires `ExpectedRevision` |
| Resolve as Apply command | **PASS** — `ResolveOperation` with Completed \| Failed \| PermitRetry |
| Vocabulary vs CONTEXT.md | **PASS** — Execution / Attempt / Operation / Blocker; Goal/Result/Failure; no "Task" as durable run |
| MAF types in module | **PASS** — zero MAF/AgentFramework references under `src/Modules/Execution` |
| Leaked internals on client-reachable contracts | **MIXED** — see findings |

**Surface detail (honest):**

- **Grain ClientEntryPoint** is correctly minimal: `Apply` / `Read` only (`IExecution.cs:5-16`).
- **Worker/custody synapses remain public wire types** in the contracts assembly with `db.execution.*` aliases: `PrepareOperation`, `TransitionOperation`, `ReadOperation`, attempt facts, user-action park/complete/deny, `IWorker`, relay/dispatch envelopes. Green labels these "internal"; they are not on the hybrid interface, but they are fireable synapses and appear in the reflected module manifest (spike pins `prepare-operation` in Facts).
- **Authorization gates** mitigate the worst of that: `Deliver` refuses non-worker Prepare/Transition (`ExecutionNeuron.cs:113-120`); ReadOperation allows worker or session only (`:122-129`). Completer-gated user-action commands.
- **`IWorker`** is a public `INeuron` (not ClientEntryPoint) — same pre-rename posture; green notes this.
- Nested Apply commands (`Start`/`Cancel`/`Resolve`) are not separately addressable RequestSynapses — only `ApplyExecution` is. Good.

Not a pure "Apply/Read only on the wire" story, but the *client grain* surface matches ratification; worker ledger verbs are intentionally fire-path APIs with caller checks.

### (2) OutcomeUncertain invariant — no auto-retry of started non-idempotent work

**What is correct:**

- Reminder path only advances when `Blocker is RetryScheduled`; explicit comment + guard (`ExecutionNeuron.Reminders.cs:27-35`).
- While `OutcomeUncertain`, attempt facts that would progress/succeed/fail/wait are ignored (`Attempts.cs:36,60,88,116`).
- Explicit `TransitionOperation` → `Uncertain` parks execution and emits `AttemptOutcomeUncertain` (`Operations.cs:142-160`).
- Cancel-with-in-flight Dispatched ops correctly forces Uncertain via `TryMarkDispatchedOperationsUncertain` (`Commands.cs:131-149, 299-335`).
- `PermitRetry` is operator-driven via `ResolveOperation`, not automatic.

**What is broken (BLOCKER):**

`AttemptFailed` with `Retryable: true` schedules `RetryScheduled` + reminder **without** inspecting the operation ledger for `Dispatched` (started) rows (`Attempts.cs:128-138`). Contrast Cancel, which refuses to leave open Dispatched effects in a retried world.

Ratified hard rule (brief + product definition): after a non-idempotent external operation **starts**, unknown outcome **never** auto-retries → `OutcomeUncertain` until reconciled.

A worker that Dispatches an external write and then reports retryable `AttemptFailed` (crash, timeout, exception path that doesn't call Transition→Uncertain) causes:

1. New attempt via reminder Accept (`Reminders.cs:37-47`)
2. Prior `Dispatched` ledger rows still present
3. Prepare of the same key returns/refreshes the in-flight row rather than refusing (`Operations.cs:37-72`)
4. Worker Accept may re-execute the external effect while the first is still unknown

The spike only covers the *cooperative* path where the worker itself marks Uncertain. It does not prove the kernel enforces the invariant when the worker fails messily after Dispatch.

### (3) Attempt-stable operation identity

| Check | Result |
|-------|--------|
| Key is worker-supplied string, not sequence+AttemptId | **PASS** — `OperationKey` string; normalize/trim only |
| Completed/Failed Prepare is idempotent return | **PASS** — `Operations.cs:39-46` |
| Uncertain Prepare refused | **PASS** — settled `NeuronAuthorizationException` `:48-52` |
| Cross-attempt reuse of same key | **PASS** mechanism — attempt stamp refresh on live rows `:60-67` |

**Caveats:**

- Identity stability is **cooperative**: workers must choose attempt-stable keys. Kernel cannot stop a worker that keys by attempt id.
- Spike `OperationKeyIsAttemptStableAcrossRetryableFailure` uses a worker that **skips** re-prepare on accept #2 (`ExecutionHarnessWorker.cs:106-136`). It asserts `PrepareCount >= 1` and phase Completed — it does **not** prove an adversarial second prepare cannot re-drive side effects. Kernel Completed short-circuit would help if the worker re-Prepares; the test never forces that path.

### (4) Bounded retention

| Store | Cap | Drop policy | Unresolved safety |
|-------|-----|-------------|-------------------|
| Command receipts | 64 | Hard-drop oldest | N/A (idempotency window shrinks) |
| Operations | 64 soft | Stop pruning when oldest is not Completed/Failed | **PASS** — Uncertain/Prepared/Dispatched never dropped (`Support.cs:153-165`) |

**No BLOCKER on silent drop of unresolved ops.** Soft-cap can grow unbounded if many live rows exist (all protected) — capacity risk, not correctness of Uncertain retention.

Receipt hard-drop means CommandId idempotency is LRU-64 only. Start still refuses double-start without receipt (`Commands.cs:46`). Re-Resolve of already-resolved key settles as refuse. Acceptable chat-style pattern.

**No automated tests** exercise the retention caps (codegraph: RememberReceipt/RememberOperation uncovered).

### (5) Rename completeness

| Check | Result |
|-------|--------|
| `src/Modules/Tasks` gone | **PASS** |
| Project/solution/composition → Execution | **PASS** |
| Grain type `execution`, state key `db.execution.state`, reminders `db.execution.*` | **PASS** |
| Wire aliases `db.execution.*` | **PASS** (manifest spike) |
| `ITask` / `TaskNeuron` / `DigitalBrain.Tasks` in `src/**/*.cs` | **PASS** — none |
| GroupChat dead `IWorker` throws | **PASS** — deleted; `IGroupChat : IAgent` only |

Flutter shell demo copy still says "cancels active Tasks" (Behavior Studio English) and demo scripts use `System.Threading.Tasks` — **not** the old module. Docs (`INTERCONNECT-REVIEW.md`) still mention TaskNeuron historically — green out-of-scope.

**No durable-state orphan risk from alias rename** given zero production callers / no real data (green claim consistent with search).

### (6) Kernel traps 2 / 3 / 4 / 8

| Trap | On ExecutionNeuron / relay | Severity |
|------|----------------------------|----------|
| **2** Zero-receiver emissions | `EmitAsync(AttemptOutcomeUncertain)` after state already written; catalog has `IHandle` on ExecutionNeuron so ghosts exist. State machine does not depend on self-delivery. | LOW — OK |
| **3** Grain-call reification | Worker dispatch is **SendAsync → relay → SendAsync** (not grain `IWorker.Accept`). `IExecution`/`IWorker` correctly **not** FrameworkInterfaces (domain, should reify). `ValidatePredecessorAsync` grain `Read()` reifies — fine. | PASS |
| **4** Settled refusals | Many deterministic validations throw `InvalidOperationException` / `ArgumentException` on **handler** paths (Prepare edge mismatch, wrong phase, invalid transition, Apply already-started, revision mismatch, etc.). Only `NeuronAuthorizationException` is `[SettledDeliveryFailure]`. Delivery of those failures **retries** (50ms × 1000). Relay correctly uses settled refusals. User-action authority correctly settled. One intentional non-settled race on CompleteUserAction "not waiting yet" (`ExecutionNeuron.cs:98`). | **HIGH** |
| **8** IHandle broadcast catalog | ExecutionNeuron declares many `IHandle<>` (attempt facts, ops, user actions) — all enter broadcast catalog. WorkerNeuron handles dispatch envelopes. Pre-existing Tasks shape; directed Send is the real path. | MEDIUM (surface expansion / ghost cost, not functional break) |

### (7) Spike honesty

| Test | Claims | Actually asserts | Honest? |
|------|--------|------------------|---------|
| DuplicateStartCommandId… | One execution | Same revision/attempt/state; **AcceptCount == 1** | **Yes** |
| ExplicitCancel… | Cancel through worker | Cancelling/Cancelled; waits for Cancelled | **Yes** (no in-flight Dispatched ops) |
| OauthStyleBlocker… | Wait + resume | Waiting + `InputRequired`; resume → Succeeded | **Yes** (simulated, not real OAuth) |
| UncertainExternalWrite… | No auto-retry + resolve | Stays `OutcomeUncertain` across 300ms; AttemptCount stable; Resolve Completed → Succeeded | **Yes for cooperative Uncertain** — does **not** cover Dispatched+retryable-fail hole |
| OperationKeyIsAttemptStable… | P0-8 | Key Completed after attempt count ≥ 2; PrepareCount ≥ 1 | **Weak** — worker avoids re-prepare on attempt 2 |
| ExecutionStateSurvivesSiloRestart | Silo restart durability | Calls `RestartSilosAsync` (real one-at-a-time silo restart); shared journal provider; asserts Waiting/revision/attempt/blocker | **Yes for grain state**; green correctly disclaims reminder re-arm |
| ExecutionManifestUsesDbExecutionAliases | Aliases | Manifest contract ids | **Yes** |

### (8) Quality

- Salvage/replace split in green report is coherent.
- `WorkerNeuron` base fills a real gap (dispatch envelope → IWorker).
- Naming is largely self-explanatory; durable key `db.execution.state` collides in *string value* with enum alias `db.execution.state` (different systems — confusing, not broken).
- No MAF, no TODO litter in the module.
- Flutter "Tasks" copy is residual English, not module residue.
- Harness `ForceUncertainWrite` synapse is unused by spikes (dead test surface, low).

---

## Findings (file:line severity)

### BLOCKER

1. **`ExecutionNeuron.Attempts.cs:128-138` BLOCKER** — `AttemptFailed` + `Retryable` schedules auto-retry (`RetryScheduled` + reminder) without `TryMarkDispatchedOperationsUncertain`. A started (`Dispatched`) non-idempotent operation can be auto-retried via a new Accept while still open. Violates ratified OutcomeUncertain invariant. Cancel path (`Commands.cs:131-149`) already has the correct pattern; failure path does not.

### HIGH

2. **`ExecutionNeuron.Operations.cs:56-57,110-111,125-126,253-278` HIGH (trap 4)** — Deterministic operation validation throws `InvalidOperationException` on the delivery/handler path → outbox retries for permanent errors (wrong phase, missing op, illegal transition). Must be `NeuronAuthorizationException` (or other `[SettledDeliveryFailure]`).

3. **`ExecutionNeuron.Commands.cs:39-46,100-106,182-188` HIGH (trap 4)** — Deterministic Apply validation (duplicate Start payload, already started, revision mismatch) throws non-settled exceptions; `HandleAsync(ApplyExecution)` is a delivery handler, so Fire path retries.

4. **`ExecutionNeuron.cs:98` HIGH (trap 4 / comment honesty)** — `InvalidOperationException` when CompleteUserAction races before park looks like intentional retry-via-storm; undocumented. Either document as deliberate eventual-retry or settle with a different design.

### MEDIUM

5. **`ExecutionSpikeProofs.cs:142-181` + `ExecutionHarnessWorker.cs:106-136` MEDIUM** — Attempt-stable spike is cooperative; does not force second-attempt Prepare/Transition against a Completed key. Overclaims relative to P0-8 "retries can't suppress duplicate side effects" if the worker misbehaves.

6. **`ExecutionSpikeProofs.cs:96-139` MEDIUM** — Uncertain spike does not cover Dispatched + retryable AttemptFailed (the hole in finding 1). Matrix row "no auto-retry" is only partially evidenced.

7. **Contracts surface (`PrepareOperation` / `TransitionOperation` / `ReadOperation` / attempt facts / `IWorker`) MEDIUM** — Public aliases in contracts assembly expand the fireable/manifest surface beyond hybrid Apply/Read. Partially mitigated by Deliver auth. Green "internal" wording is softer than the wire reality.

8. **`WorkerNeuron.cs:20-38` MEDIUM** — Dispatch envelopes invoke Accept/Continue/Cancel with no caller authentication on the worker. Any neuron that can Send to the worker grain can drive verbs. Pre-rename risk retained.

9. **Trap 8 catalog load MEDIUM** — Broad `IHandle<>` set on `ExecutionNeuron` puts ops + attempt facts into broadcast catalog (ghost cost on every Emit of those types).

### LOW

10. **`ExecutionNeuron.Support.cs:153-165` LOW** — Operation ledger soft-cap can grow past 64 when many live rows exist (correctly refuses to drop them). No test for cap behavior.

11. **`ExecutionNeuron.cs:29` + `ExecutionState.cs:4` LOW** — Shared string `db.execution.state` for durable key and enum serializer alias; easy to misread in diagnostics.

12. **Flutter shell Behavior copy** (`behavior_overview.dart` / view model) LOW — English "Tasks" remains; not module residue; optional cleanup.

13. **No PIN-DEFECT(P0-8) history** LOW — Green characterized green pins without prior RED pins; acceptable given brief "characterize-as-you-go," but grill cannot compare against a red baseline.

---

## What is solid (credit)

- Rename is complete for live code; composition, grain type, state names, aliases are Execution-shaped.
- Hybrid Apply/Read ClientEntryPoint shape matches ratification; Cancel/Resolve nested correctly.
- Cooperative OutcomeUncertain path + ResolveOperation reconciliation work and are tested.
- Reminder path correctly refuses to advance on OutcomeUncertain.
- Attempt-stable key design (string key, Completed short-circuit) is the right P0-8 fix direction.
- Bounded ops retention protects unresolved ledger rows.
- Worker relay uses settled refusals and outbox Send (not illicit grain Accept).
- GroupChat dead supervised throws removed cleanly.
- Gate green 131/131; restart spike really restarts silos; duplicate submission really checks AcceptCount.

---

## Required fixes before APPROVE

1. On **retryable** `AttemptFailed` (and any other auto-retry admission path): if any operation is `Dispatched` (or otherwise started-without-terminal-outcome), force `OutcomeUncertain` and **do not** schedule `RetryScheduled` — mirror Cancel's `TryMarkDispatchedOperationsUncertain`.
2. Convert deterministic handler refusals to `NeuronAuthorizationException` (trap 4).
3. Strengthen spikes: (a) Dispatched + retryable fail must stay Uncertain with stable AttemptCount; (b) second attempt re-Prepare of Completed key returns Completed and does not double external effect.

---

## Out of scope (noticed, not judged as S1.4 blockers)

- Conversation adapter (S1.5) still must implement a real `IWorker`.
- Docs still say Tasks/TaskNeuron historically.
- Behavior Studio Flutter fixtures.

---

VERDICT: REJECT
