# S1.4-GREENB — close S14-grill rejection

**Subject:** S1.4 GRILL three numbered demands  
**Role:** GREEN (surgical fix; no git)  
**Grill:** `plans/stage1/reports/S14-grill.md`  
**Prior green:** `plans/stage1/reports/S14-execution-green.md`

---

## Demand → fix → test

| # | Grill demand | Fix | Test |
|---|--------------|-----|------|
| **1** | BLOCKER: on retryable `AttemptFailed` (and every auto-retry admission path incl. reminder redispatch), if any operation is `Dispatched` / started-without-terminal-outcome → force `OutcomeUncertain`, do **not** schedule `RetryScheduled` — mirror Cancel's `TryMarkDispatchedOperationsUncertain` | **`ExecutionNeuron.Attempts.cs`**: before auto-retry, call `TryMarkDispatchedOperationsUncertain`; on hit set `Waiting` + `OutcomeUncertain`, unregister retry reminder, `SaveAsync` + `EmitAsync(AttemptOutcomeUncertain)`, return. **`Reminders.cs`**: defense-in-depth — if `RetryScheduled` fires while any op is still `Dispatched`, mark Uncertain and refuse redispatch. **`Commands.cs`**: `TryMarkDispatchedOperationsUncertain` no longer requires `ActiveAttempt` (reminder defense). | **`DispatchedRetryableFailForcesOutcomeUncertainWithoutAutoRetry`** — worker Dispatches then sends retryable `AttemptFailed` without cooperative Uncertain; asserts `OutcomeUncertain`, stable `AttemptCount`, AcceptCount stays 1 across 300ms retry window, op phase `Uncertain`. |
| **2** | Convert deterministic handler refusals to settled `NeuronAuthorizationException` (trap 4) | Operations validation (`edge mismatch`, missing op, expected-phase, `RequireActiveAttempt`, `ValidateEdge`/`Reference`/`Transition`) → settled. Apply/Start/Cancel/Resolve deterministic refusals (duplicate start, revision mismatch, missing ExpectedRevision, bad resolution, predecessor) → settled. **Left intentional non-settled:** `CompleteUserAction` park race (`ExecutionNeuron.cs`) — documented as delivery-retry-until-Waiting. | Covered by existing spikes + suite (settled path is compile/behavior; no new pin required). Trap-4 violations would have caused delivery retry storms under fire. |
| **3a** | Strengthen spike: Dispatched + retryable fail stays Uncertain, stable AttemptCount, no reminder-driven retry | Same as demand 1 production path. Harness script `DispatchThenRetryableFail`. | **`DispatchedRetryableFailForcesOutcomeUncertainWithoutAutoRetry`** |
| **3b** | Strengthen spike: second-attempt re-Prepare of Completed key returns recorded completion; never re-executes external effect (counting worker) | Harness `CompleteThenRetryableFail`: accept #1 Prepare+Dispatch+Complete+retryable fail; accept #2 **always re-Prepares** same key, skips external effect, succeeds. `ExternalEffectCount` only increments on real Dispatch. | **`OperationKeyIsAttemptStableAcrossRetryableFailure`** — wait Succeeded; phase Completed; `PrepareCount >= 2`; `ExternalEffectCount == 1`; `AcceptCount >= 2`. |

---

## What changed

| Path | Change |
|------|--------|
| `src/Modules/Execution/Execution/ExecutionNeuron.Attempts.cs` | Retryable fail + Dispatched → OutcomeUncertain (no RetryScheduled) |
| `src/Modules/Execution/Execution/ExecutionNeuron.Reminders.cs` | Reminder redispatch refuses/marks Uncertain if Dispatched ops remain |
| `src/Modules/Execution/Execution/ExecutionNeuron.Commands.cs` | Settled Apply refusals; TryMark without ActiveAttempt gate |
| `src/Modules/Execution/Execution/ExecutionNeuron.Operations.cs` | Settled operation validation refusals |
| `src/Modules/Execution/Execution/ExecutionNeuron.Support.cs` | Settled Start validation / predecessor refusals |
| `src/Modules/Execution/Execution/ExecutionNeuron.cs` | Document intentional non-settled park race |
| `src/Tests/.../Harness/ExecutionHarnessWorker.cs` | `DispatchThenRetryableFail`, `ExternalEffectCount`, adversarial re-Prepare |
| `src/Tests/.../ExecutionSpikeProofs.cs` | New 3a spike; strengthened 3b spike |

---

## Tests

| Test | Role |
|------|------|
| `DispatchedRetryableFailForcesOutcomeUncertainWithoutAutoRetry` | **NEW** — demand 1 + 3a |
| `OperationKeyIsAttemptStableAcrossRetryableFailure` | **STRENGTHENED** — demand 3b (`PrepareCount >= 2`, `ExternalEffectCount == 1`) |
| Prior Execution spikes | Regression (dup start, cancel, OAuth wait, cooperative Uncertain, restart, manifest) |

---

## Gate

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  (AppHost node NO_COLOR/FORCE_COLOR noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 132, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~49s
```

ChartVocabularyProofs did not flake this run.

---

## Conflicts & risks

- **Non-retryable** `AttemptFailed` with open `Dispatched` rows still goes terminal Failed without forcing Uncertain (grill scoped demand to retryable path). Soft retention keeps the ledger rows; Resolve refuses terminal executions — pre-existing gap, out of scope.
- Intentional non-settled park race on `CompleteUserAction` retained (grill finding 4 / HIGH comment honesty) — not in the three numbered required fixes as a convert-to-settled item.
- Second-attempt worker still *chooses* not to re-Dispatch after re-Prepare; kernel Completed short-circuit is exercised by re-Prepare (`PrepareCount >= 2`) and counting proves effect ran once. Fully adversarial “try Transition Prepared→Dispatched on Completed” would settle-refuse under demand 2 — not required for the counting proof.

---

## Out of scope

- Conversation adapter `IWorker` (S1.5)
- Trap 8 broadcast catalog cost, public worker-ledger wire surface, WorkerNeuron Accept auth (grill MEDIUM)
- Flutter “Tasks” English, historical docs
- Operation retention cap tests
- Non-retryable-Dispatched reconciliation design
