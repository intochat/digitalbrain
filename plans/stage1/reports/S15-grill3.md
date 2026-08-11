# S1.5-GRILL-3 — final re-grill of GREEN-c closers

**Subject:** `26b5e2a3` S1.5-GREEN-c  
**Prior:** GRILL-2 `S15-grill2.md` (REJECT) · GREEN-c report `S15-turnsc.md`  
**Role:** GRILL-3 (judge only; no production edits; no git writes)  
**Scope:** Verify ONLY the four grill2 **Required before APPROVE** items are truly closed; quick regression sweep; gate.

---

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  2 Warning(s) — AppHost node NO_COLOR/FORCE_COLOR noise only (not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 152, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~181s
```

ChartVocabularyProofs and restart proofs did not flake this run. Gate green alone does not authorize APPROVE — code + test honesty do.

---

## Required item (1) — bridge authority / forged ExecutionTerminal

### Code: re-Read on every apply path?

| Path | Re-Reads `IExecution`? | Payload Result/Failure applied? |
|------|------------------------|----------------------------------|
| Live bridge `ReconcileFromExecutionTerminalAsync` → timer → `ApplyTerminalWakeupFromKernelReadAsync` | **Yes** (`Chat.cs:310–312`) | **No** — Result/Failure only from Read snapshot (`:336–337` → `ApplyExecutionSnapshotToTurnAsync`) |
| Mismatch `terminal.Revision` / `terminal.State` vs Read | **Ignore settled** (`:319–322`) | No apply |
| Activation reconcile `ReconcileActiveExecutionAsync` | **Yes** (`:387–389`, re-Read after cancel `:447–449`) | Snapshot only |
| Waiting-deadline `FailWaitingTurnAfterPolicyDeadlineAsync` | **Yes** (`:554–556`, re-Read after cancel `:604–606`) | Snapshot only |

Delivery turn only queues the wake-up (`:260–266`); apply is deferred to a grain-timer turn to avoid nested re-Read deadlock — authority is still kernel Read, never free-form payload Result.

### Test: `ForgedExecutionTerminalIsIgnoredWithoutKernelConfirmation`

Meaningful attack matrix:

| Forge | Asserted |
|-------|----------|
| Wrong ExecutionId + fabricated `Succeeded` + `FORGED-WRONG-ID` | Ignored (no matching turn) |
| Real ExecutionId + **mismatched revision** (`real.Revision + 100`) + fabricated success Result `FORGED-RESULT` | Turn stays **Running**; no transcript `FORGED*` |
| Real ExecutionId + **matching revision** but **wrong State** (`Succeeded` while kernel still Running) + `FORGED-STATE-MISMATCH` | Same — no apply (state mismatch) |

Also asserts eventual honest Completes after agent release (live path still works).

**Judgment:** Grill2 finding 1 **CLOSED**. Forgery cannot inject transcript or force terminal without kernel confirmation.

---

## Required item (2) — Waiting / OutcomeUncertain deadline

### Durable across restart (reminder, not timer)?

| Mechanism | Primitive | Evidence |
|-----------|-----------|----------|
| Policy deadline | **Orleans reminder** `chat.waiting-deadline` | `SurfaceWaitingTurnAsync` → `RegisterOrUpdateReminder` (`Chat.cs:513–517`); `IRemindable.ReceiveReminder` (`:340–349`) |
| Wake-up batching | Grain timer (non-durable) | Only defers re-Read; not the park deadline |
| Deadline retry while Cancelling | Re-arms same reminder (`:642–647`) | Head cannot silently drop off the deadline |

Reminder is the production-durable primitive. Test cluster uses in-memory reminders (same as kernel liveness proofs); that does not demote the product choice.

### Cancel path if worker ignores cancel?

| Layer | Behavior |
|-------|----------|
| Chat deadline | `CancelExecution` then re-Read; if still non-terminal → mark Cancelling + re-arm reminder |
| Kernel | After cancel dispatch, re-arms 15s liveness while still Cancelling (`ExecutionNeuron.Commands.cs:168–178`) |
| FailAbandoned | Covers **`Running` or `Cancelling`** with no PendingDispatch (`Dispatch.cs:171`) → `WorkerAbandoned` + `NotifyOriginOfStateAsync` |

Proofs:

- `OutcomeUncertainSurfacesWaitingAndPolicyDeadlineUnfreezesFifo` — surfaces `Waiting` + lifecycle; head becomes `Failed|Cancelled`; next Completes (FIFO unfrozen).
- `StuckCancellingIsFailedByLivenessAsWorkerAbandoned` — harness `IgnoreCancel` (no `AttemptCancelled`); 15s liveness → `Failed` + `WorkerAbandoned` (no silo restart).

**Judgment:** Grill2 finding 2 **CLOSED**. OutcomeUncertain no longer freezes FIFO forever without a durable escape hatch; ignore-cancel Cancelling is covered by liveness.

---

## Required item (3) — pure worker-death / liveness

### Test: `PureWorkerLivenessFailsWithWorkerAbandonedAndAdvancesQueue`

| Check | Result |
|-------|--------|
| Calls `RestartSilosAsync`? | **No** |
| Holds agent, waits for kernel | Yes — `exec.State == Failed && Failure is WorkerAbandoned` |
| Head Failed | Yes |
| Queue advances (next Completes) | Yes |
| Distinct from `KilledWorkerReachesFailedAndQueueAdvances` | Yes — that one still restarts silos; pure path is separate |

Kernel path: Accept registers 15s liveness (`Attempts.cs:21`); `FailAbandonedRunningIfNeededAsync` → `WorkerAbandoned` when no Dispatched ops.

**Judgment:** Grill2 finding 6 (test honesty) **CLOSED**.

---

## Required item (4) — revision idempotency

### Code

`DurableTurnRecord.AppliedExecutionRevision` set on first terminal apply (`Chat.cs:739`). Same revision + already terminal → early return, **no** `TryEmitRespondedAsync`, **no** extra `TurnLifecycle` (`:686–701`). Already-terminal at a *different* revision also skips re-Responded (`:704–719`).

### Test: `DuplicateExecutionTerminalIsIdempotentByRevision`

Fires two legitimate matching `ExecutionTerminal`s (kernel State/Revision/Result) after Completes:

- Assistant transcript lines stay **1** (`scripted:idem-agent`)
- Outgoing `TurnLifecycle` Completed count **unchanged**
- Status stays Completed

**Judgment:** Grill2 finding 4 (idempotent re-apply) **CLOSED**.

---

## Regression sweep (earlier seam proofs)

`git show HEAD -- DurableTurnProofs.cs` is **append-only** for the four new facts; prior bodies not weakened:

| Prior proof | Still asserts | Weakened? |
|-------------|----------------|-----------|
| `FifoQueueRunsTurnsInArrivalOrderWithinOneConversation` | Head Running / next Pending; AcceptCount gates | No |
| `CancelRunningTurnIsVersionedAndIdempotent` | Cancelling, AcceptCount flat until terminal, WasCancelled, one reply | No |
| `CancelQueuedTurnAdvancesTheQueue` | Queued → Cancelled while head Running | No |
| `SendReturnsTurnIdAndCompletesThroughExecutionIndependentlyOfObserverAbort` | Abort then Completes (P0-2) | No |
| Composition pins (no CompleteTurnWork, directed worker, allow-list) | Intact | No |
| `RunningTurnSurvivesSiloRestartAndCompletes` | Terminal head + queue progress | No (unchanged strength) |

No PIN-DEFECT introduced to paper over failures.

---

## Scorecard vs grill2 Required before APPROVE

| # | Required item | Status |
|---|---------------|--------|
| **1** | Bridge authority + forgery proof | **CLOSED** |
| **2** | Waiting freeze + durable deadline + Cancelling liveness | **CLOSED** |
| **3** | Pure worker-death WorkerAbandoned without silo restart | **CLOSED** |
| **4** | Revision-idempotent re-apply (no double Responded / lifecycle) | **CLOSED** |

---

## Residuals (do not reopen APPROVE; ride)

- Fire-and-return Accept concurrency outside grain turn (grill2 finding 5) — unchanged, out of GREEN-c scope.
- Test-cluster in-memory reminders are not durable silo-restart storage; production reminder service is the intended durability plane.
- Waiting-deadline product test accepts `Failed|Cancelled` (not specifically `WorkerAbandoned`); stick-Cancelling path has its own explicit WorkerAbandoned proof.
- Grain-timer wake-up queue is in-memory; loss before process is recovered by activation re-Read / kernel re-notify — not a free-form trust hole.

---

## What is solid (final)

- ExecutionTerminal is wake-up only; every apply path re-Reads kernel; forged Result never lands.
- OutcomeUncertain surfaces Waiting, arms durable policy reminder, cancel-unsticks + Cancelling liveness.
- Pure liveness path proven without silo restart.
- Duplicate terminal delivery is revision-idempotent.
- Prior FIFO / cancel / observer-detach proofs not weakened.
- Gate 152/152 this session.

---

## VERDICT: APPROVE
