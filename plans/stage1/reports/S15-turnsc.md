# S1.5-GREEN-c — close S15-grill2 REJECT (bridge authority + abandonment)

**Role:** GREEN  
**Authority:** `GROK.md`, `plans/stage1/reports/S15-grill2.md` Required before APPROVE (1–4)  
**Prior:** GREEN-b `S15-turnsb.md` · GRILL-2 `S15-grill2.md` (REJECT)

---

## Item → fix → test

| # | Grill2 required item | Fix | Test |
|---|----------------------|-----|------|
| **1** | Bridge authority: `ExecutionTerminal` is wake-up only; re-Read kernel; forged wrong id/revision/state ignored settled | **Chat** queues wake-up, re-Reads `IExecution` on a grain-timer turn (avoids mid-delivery nested re-Read deadlock), applies **only** Read State/Result/Failure. Confirms `terminal.Revision` + `terminal.State` match Read; else ignore settled. | `ForgedExecutionTerminalIsIgnoredWithoutKernelConfirmation` |
| **2** | Abandonment freeze: Waiting/OutcomeUncertain must not freeze FIFO forever — surface + durable policy deadline → CancelExecution+bridge | **ChatTurnStatus.Waiting**; surface via `TurnLifecycle` + durable turn status; **IRemindable** `chat.waiting-deadline` (15s, kernel-scale); on expiry CancelExecution + bridge. Activation reconcile also surfaces Waiting. | `OutcomeUncertainSurfacesWaitingAndPolicyDeadlineUnfreezesFifo` (surfaces Waiting + lifecycle; deadline unfreezes; next Completes) |
| **3** | Pure worker-death / liveness (no full silo restart) → WorkerAbandoned + queue advance | Kernel 15s liveness after Accept already present; test holds AI, **no** `RestartSilosAsync`, asserts `WorkerAbandoned` on Execution + head Failed + next Completes. | `PureWorkerLivenessFailsWithWorkerAbandonedAndAdvancesQueue` |
| **4** | Idempotent re-apply by ExecutionId+Revision; FailAbandoned for stuck Cancelling | **DurableTurnRecord.AppliedExecutionRevision** — same revision terminal re-apply is a no-op (no re-Responded / no extra lifecycle). **FailAbandoned** covers `Cancelling` + no PendingDispatch; Cancel re-arms 15s liveness after cancel dispatch. | `DuplicateExecutionTerminalIsIdempotentByRevision`; `StuckCancellingIsFailedByLivenessAsWorkerAbandoned` |

---

## What changed

| Path | Change |
|------|--------|
| `Chat/ChatTurnStatus.cs` | `Waiting = 6` |
| `Chat/Chat.cs` | Wake-up queue + grain-timer re-Read apply; Waiting surface + `IRemindable` policy deadline; AppliedExecutionRevision idempotency; activate reconcile for Waiting |
| `Chat/ChatTurnWorker.cs` | Test seam `ConfigureLeaveDispatchedOperation` to leave a Dispatched op (OutcomeUncertain park) |
| `ExecutionNeuron.Dispatch.cs` | `FailAbandonedRunningIfNeededAsync` covers Cancelling |
| `ExecutionNeuron.Commands.cs` | After cancel dispatch, re-arm 15s liveness while still Cancelling |
| `ExecutionHarnessWorker.cs` | `IgnoreCancel` script (no AttemptCancelled) for stuck-Cancelling proof |
| `DurableTurnProofs.cs` | Forgery, idempotent, pure liveness, Waiting-deadline proofs |
| `ExecutionSpikeProofs.cs` | Stuck Cancelling → WorkerAbandoned |

---

## Architecture note

```
ExecutionTerminal (directed) → Chat queues wake-up
  → grain timer turn: Read IExecution
  → if terminal.Revision/State ≠ Read: ignore settled
  → if Waiting: surface ChatTurnStatus.Waiting + arm chat.waiting-deadline
  → if Succeeded/Failed/Cancelled: apply Read result once (AppliedExecutionRevision)
  → duplicate same revision: no-op

Waiting park expiry:
  chat.waiting-deadline → CancelExecution → bridge terminal → TryStartNext
```

Re-Read is **not** done inside the inbound delivery turn (green-b deadlock class: nested Execution recovery / worker Accept re-entering Chat). Authority is still kernel Read; payload is never applied as free-form Result text.

---

## Gate

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  2 Warning(s) — AppHost node NO_COLOR/FORCE_COLOR noise only (not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 152, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~180s
```

ChartVocabularyProofs did not flake this run.

---

## Conflicts & risks

- Waiting policy deadline is a fixed 15s (mirrors kernel liveness), not per-owner config.
- `ChatTurnWorker.ConfigureLeaveDispatchedOperation` is a test seam on production code (InternalsVisibleTo) — same pattern family as ScriptedAgent static holds.
- Fire-and-return Accept concurrency residual (grill2 finding 5) unchanged; out of this close-out scope.

---

## Out of scope

- Cancel-wins-always (refuse Succeeded while Cancelling)
- POST observer `afterSequence: 0`
- P0-6 tool safe-point composition residual
- Full catalog/graph unification
