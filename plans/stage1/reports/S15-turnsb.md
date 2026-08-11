# S1.5-GREEN-b — terminal bridge report

**Role:** GREEN  
**Brief:** `plans/stage1/briefs/S15-greenb-fixes.md`  
**Grill:** `plans/stage1/reports/S15-grill.md` (REJECT findings 1–6)

---

## Finding → fix → test

| # | Grill finding | Fix | Test |
|---|---------------|-----|------|
| **1** | No activation recovery for Running head; restart freezes FIFO | **Chat** `OnNeuronActivatedAsync` → `ReconcileActiveExecutionAsync`: re-Read linked Execution; terminal → apply + advance; missing → Fail; after silo restart, stuck Running/Pending/Cancelling is driven via `CancelExecution` then bridge. **DelayDeactivation** while a turn is active so live AI is not cancelled by idle reactivation. | `RunningTurnSurvivesSiloRestartAndCompletes` — asserts **terminal** head after restart and queue progress |
| **2** | Cancel of running clears active and starts next before worker stops | Running cancel: `CancelExecution` + mark **Cancelling**; **do not** clear `ActiveTurnId` or `TryStartNext`. Only terminal bridge advances. **ChatTurnWorker** `BeginAccept` returns immediately so `DispatchWorkerCancel` interleaves; Cancel always acks `AttemptCancelled`. | `CancelRunningTurnIsVersionedAndIdempotent` — Cancelling while next stays Pending; agent cancelled; one reply only (queued turn) |
| **3** | Worker death leaves stuck Running | Kernel liveness reminder after Accept; `FailAbandonedRunningIfNeededAsync` on dispatch/retry reminders. Chat restart recovery cancels abandoned heads so the bridge fires. | `KilledWorkerReachesFailedAndQueueAdvances` — restart mid-hold → terminal head + next Completes |
| **4** | Relay allows any same-owner grain type | `WorkerGrainTypeRegistry` allow-list (`worker`, `chat-turn-worker`) registered in `ExecutionModule` / `IWorkerTypeRegistration`. Unregistered type → settled `worker-type-not-registered`. | `UnregisteredWorkerTypeIsRefusedByDispatchRelay` + composition pin |
| **5** | Spoofable `CompleteTurnWork` | **Removed**. Completion is `Attempt*` → Execution terminal → directed `ExecutionTerminal` → Chat re-applies from payload (wake-up; no trust of free-form status/text without Execution). | Composition: no `CompleteTurnWork`; `ExecutionNotifiesOriginOnSuccess`; chat Responded via bridge |
| **6** | Trap 8: `ChatTurnWorker` `IHandle<DispatchWorker*>` | Restructured to **directed only** (`OnUnboundSynapseAsync`). DispatchWorker* no longer catalogued via the adapter. | `ChatTurnWorkerUsesDirectedDispatchWithoutBroadcastIHandle` |

### Riding (findings 7–9)

| # | Status |
|---|--------|
| **7** | POST observer `afterSequence: 0` — unchanged (MEDIUM) |
| **8** | Tool safe-point residual / composition-only P0-6 pin — unchanged (MEDIUM) |
| **9** | `AttemptAccepted` outbox lag until Accept background work drains — reduced by fire-and-return Accept; residual acceptable (LOW) |

---

## What changed

| Path | Change |
|------|--------|
| `Execution/Contracts/ExecutionTerminal.cs` | Directed wake-up fact `{ExecutionId, State, Revision, Result?, Failure?}` |
| `Execution/Contracts/ExecutionCommands.cs` | `StartExecution.Origin` |
| `Execution/Contracts/Result.cs` / `Failure.cs` | `ChatTurnResult` / `ChatTurnFailure` in contracts for persistence round-trip |
| `Execution/Contracts/WorkerAbandoned.cs` | Failure when attempt abandoned |
| `Execution/ExecutionData.cs` | Durable `Origin` |
| `ExecutionNeuron.*` | Origin on start; `NotifyOriginOfStateAsync` on terminal/Waiting; Save before notify; activation recovery (pending dispatch / cancel only — no nested Accept); liveness reminder + `FailAbandonedRunningIfNeededAsync` |
| `WorkerGrainTypeRegistry.cs` / `ExecutionModule.cs` | Allow-list composition |
| `WorkerDispatchRelayNeuron.cs` | Enforce allow-list settled |
| `Chat.cs` | Origin on Start; Cancelling cancel; activation reconcile; `ExecutionTerminal` handler (no re-Read deadlock); remove CompleteTurnWork |
| `ChatTurnWorker.cs` | OnUnbound directed dispatch; non-blocking Accept; Attempt* only; always-ack Cancel |
| `ChatTurnGoal.cs` | Result/Failure types moved to contracts |
| `UiModule.cs` | Registers `chat-turn-worker` worker type |
| `ChatTurnStatus.cs` | `Cancelling` |
| `DurableTurnProofs.cs` | Hardened restart/cancel/kill tests + composition pins |
| `ExecutionTerminalBridgeProofs.cs` | Origin gets `ExecutionTerminal` on success |

---

## Tests

| Test | Role |
|------|------|
| `RunningTurnSurvivesSiloRestartAndCompletes` | **STRENGTHENED** — terminal after restart |
| `KilledWorkerReachesFailedAndQueueAdvances` | **NEW** — abandoned head terminal + queue advances |
| `CancelRunningTurnIsVersionedAndIdempotent` | **STRENGTHENED** — Cancelling, no double Accept, one reply |
| `UnregisteredWorkerTypeIsRefusedByDispatchRelay` | **NEW** |
| `ExecutionNotifiesOriginOnSuccess` | **NEW** |
| Composition pins (CompleteTurnWork gone, Origin, allow-list, OnUnbound) | **NEW/UPDATED** |
| Prior durable/FIFO/actor/chat responder suite | Regression |

---

## Gate

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  (AppHost node NO_COLOR/FORCE_COLOR noise only — not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 147, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~116s
```

ChartVocabularyProofs did not flake this run.

---

## Architecture note (terminal bridge)

```
Chat.StartExecution(Origin=chat)
  → Execution dispatch → ChatTurnWorker.Accept (returns immediately)
  → worker AI → AttemptSucceeded/Failed/Cancelled
  → Execution terminal + Save + Send(ExecutionTerminal → Origin)
  → Chat applies status/transcript from terminal payload; advances FIFO
```

Chat never trusts a free-form completion push. Kernel is source of truth; Chat only reconciles.

**Deadlock avoided:** terminal handler does **not** re-Read Execution mid-turn (activation recovery + worker Accept could re-enter Chat). Payload carries Result/Failure at transition time; activation reconcile still re-Reads for restart recovery.

---

## Conflicts & risks

- Restart recovery uses **CancelExecution** to unstick Running heads after silo restart (in-memory reminders do not survive). Owner-visible status may be Cancelled rather than Failed for abandoned AI; FIFO still advances. Liveness reminder (`FailAbandonedRunning`) covers non-restart worker death when reminders remain.
- Fire-and-return Accept mutates grain fields from a background task; Cancel/Accept serialization still holds for grain methods, but SendAsync from background uses `_handling is null` commit path. DelayDeactivation keeps the activation alive for the AI stream.
- `ExecutionSnapshot` constructed in the terminal handler uses placeholder Goal/Policy — only State/Result/Failure/Revision are consumed.

---

## Out of scope

- Findings 7–9 (observer scan, P0-6 restore proof, AttemptAccepted drain lag)
- Full catalog/graph unification
- Behavior Studio host
- Surface-events → windowing bridge
