# S1.4-GRILL2 — re-grill OutcomeUncertain auto-retry closure

**Subject:** `80829c39` S1.4-GREEN-b (closes `S14-grill.md` REJECT)  
**Role:** GRILL (judge only; no code or git changes)  
**Green-b report:** `plans/stage1/reports/S14-execution-greenb.md`  
**Prior grill:** `plans/stage1/reports/S14-grill.md` (VERDICT: REJECT)

---

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  (AppHost node NO_COLOR/FORCE_COLOR noise only — not C# warnings)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 132, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~47s
```

ChartVocabularyProofs did not flake this run. Suite is green.

---

## Scope of this re-grill

Prior grill required three numbered fixes before APPROVE:

1. **BLOCKER** — retryable `AttemptFailed` (and every auto-retry admission path) must not schedule `RetryScheduled` / open a new Accept while any operation is `Dispatched` / started-without-terminal-outcome; force `OutcomeUncertain` instead.
2. Deterministic handler refusals → settled `NeuronAuthorizationException` (trap 4).
3. Strengthen spikes: (a) Dispatched + retryable fail stays Uncertain / stable AttemptCount; (b) second-attempt re-Prepare of Completed key does not re-execute external effect.

This session attacks those closures only. Prior MEDIUM surface/auth/catalog notes remain background, not re-litigated as S1.4 blockers unless they regressed.

---

## Attack matrix — every path that can admit a new attempt

**Definition of "new attempt" for this attack:** a new `ActiveAttempt` + `AttemptCount++` and/or a new `AcceptWorkerDispatch` (worker `Accept`).  
**Not a new attempt:** same-attempt `ContinueWorkerDispatch` / `CancelWorkerDispatch`; operator `ResolveOperation`.

| Admission path | Where | Can run while any op is Dispatched? | Judgment |
|----------------|-------|--------------------------------------|----------|
| **(A) AttemptFailed retryable → RetryScheduled** | `ExecutionNeuron.Attempts.cs:121-161` | **No.** `mayAutoRetry && TryMarkDispatchedOperationsUncertain` forces `Waiting` + `OutcomeUncertain`, unregisters retry reminder, emits `AttemptOutcomeUncertain`, **returns before** `RetryScheduled`. Only when no Dispatched rows remain does it register the reminder. | **CLOSED** — primary hole fixed |
| **(B) Reminder redispatch on RetryScheduled** | `ExecutionNeuron.Reminders.cs:27-68` | **No (defense-in-depth).** Guard still requires `Waiting` + `RetryScheduled` + budget. Before `AcceptWorkerDispatch` / `AttemptCount++`, if any op is Dispatched → mark Uncertain, clear pending dispatch, unregister retry, emit fact, return. | **CLOSED** — cannot open Accept under open Dispatched even if stale state ever had RetryScheduled |
| **(C) Worker re-Accept** | Only via `AcceptWorkerDispatch` from Start or reminder (A/B) | Worker cannot invent a new attempt; relay Accept is kernel-scheduled. With A+B closed, re-Accept under open Dispatched cannot be scheduled. | **CLOSED** |
| **(D) ResolveOperation PermitRetry** | `Commands.cs:254-270` | **Operator path, not auto-retry.** Requires op already `Uncertain` and execution blocked on `OutcomeUncertain`. Reverts **that** key to `Prepared` and schedules **Continue** on the **same** `ActiveAttempt` (not Accept, not AttemptCount++). Other still-Dispatched rows (if any) are not auto-retried as a new attempt; operator consciously unblocks. | **PASS** for auto-retry invariant |
| **(E) Cancel-then-restart** | Cancel `Commands.cs:131-149`; Start `Commands.cs:26-86` + `ValidatePredecessorAsync` | Cancel with Dispatched → `OutcomeUncertain` (not terminal) + Cancel dispatch, not a new Accept. Fresh Start on a **new** grain only when unstarted; same grain refuses second Start (settled). `RetryOf` predecessor must be **terminal** — Waiting/Uncertain cannot be a predecessor, so cancel-with-open-Dispatched cannot seed a clean RetryOf retry. | **CLOSED** |
| **(F) Start first Accept** | `StartAsync` | Empty ops ledger; first attempt only. | N/A |
| **(G) AttemptProgressed / Resolve Completed → Continue** | same attempt | Not a new attempt; OutcomeUncertain facts still ignored while blocked. | N/A |

**Cross-checks that seal A/B:**

- After messy-fail Uncertain path, `ActiveAttempt` is **retained** (not nulled) and `Blocker` is `OutcomeUncertain` → `IsOutcomeUncertain` causes later progress/success/fail facts to no-op; reminder path requires `RetryScheduled`, so no redispatch.
- After clean retryable fail (no Dispatched), `ActiveAttempt` is nulled before `RetryScheduled` — worker Prepare/Transition on the dead attempt cannot match; reminder mints a new attempt only then.
- Single-threaded grain turns: cannot Dispatch after scheduling RetryScheduled without an active attempt (RequireActiveAttempt / Matches refuse).

**Residual (not auto-retry, not this grill's BLOCKER):** non-retryable `AttemptFailed` with open Dispatched still goes terminal `Failed` without forcing Uncertain; Resolve refuses terminal executions. Soft ledger retains the rows. Acknowledged in green-b; no auto-retry violation.

---

## Spike honesty (diff-read)

### 3a — `DispatchedRetryableFailForcesOutcomeUncertainWithoutAutoRetry` (**NEW**)

Harness: `DispatchThenRetryableFail` — Prepare + Transition→Dispatched + external-effect count, then retryable `AttemptFailed` **without** Transition→Uncertain.

| Assertion | Binds? |
|-----------|--------|
| State → Waiting with `OutcomeUncertain` | Yes — kernel forced Uncertain |
| `AttemptCount` stable across 300ms window | Yes — no reminder-driven attempt |
| `AcceptCount == 1` after window | Yes — **no second Accept** (the auto-retry pin) |
| `ReadOperation("messy-write").Phase == Uncertain` | Yes — ledger row marked Uncertain |

Does **not** assert `ExternalEffectCount` on this path; with Accept pinned at 1 and script only dispatching on Accept, a second external write cannot run. Honest for demand 1 + 3a.

### 3b — `OperationKeyIsAttemptStableAcrossRetryableFailure` (**STRENGTHENED**)

Harness: `CompleteThenRetryableFail` — accept #1 Prepare+Dispatch+Complete+retryable fail; accept #2 **always re-Prepares** same key, skips effect when told.

| Assertion | Binds? |
|-----------|--------|
| Wait until `Succeeded` | Yes — retry path completed |
| Op phase `Completed`, key stable | Yes |
| `PrepareCount >= 2` | Yes — second attempt **did** re-Prepare (adversarial) |
| `ExternalEffectCount == 1` | Yes — external write counted only on real Dispatch path |
| `AcceptCount >= 2` | Yes — auto-retry after Completed (no open Dispatched) did fire |

**Honesty caveat (LOW, not a reject):** second Accept sets `executeExternalEffect: false` after Prepare rather than attempting Transition Prepared→Dispatched against Completed. Kernel short-circuit is still exercised (Prepare returns existing Completed; overwriting would fail the final Completed assert). A fully adversarial Transition would settle-refuse under trap-4 `ValidateTransition` — green-b documents this. Demand 3b as stated is met.

---

## Trap 4 — settled refusals

Prior HIGH findings (ops validation + Apply Start/Cancel/Resolve deterministic refusals) are **NeuronAuthorizationException** on:

- `Operations.cs` — edge mismatch, missing op, expected-phase, Uncertain re-prepare, ValidateEdge/Reference/Transition, RequireActiveAttempt
- `Commands.cs` — unknown apply command, duplicate Start payload, already started, revision/ExpectedRevision, Resolve validation
- `Support.cs` — policy/worker/predecessor validation

`NeuronAuthorizationException` carries `[SettledDeliveryFailure]` (`DigitalBrain.Abstractions`).

**Intentional non-settled retained (documented):** CompleteUserAction park race (`ExecutionNeuron.cs:94-100`) — delivery-retry-until-Waiting. Prior grill HIGH #4 / green-b risk note; not in the three numbered required convert-to-settled items.

**Remaining InvalidOperationException** are programming/invariant errors (unknown reminder name, unsupported pending dispatch shape, Load before start, Cursor without ActiveAttempt) — not deterministic client validation refusals.

---

## Module surface regression

Green-b diff touches **implementation + tests only** (`Attempts` / `Reminders` / `Commands` / `Operations` / `Support` / `ExecutionNeuron.cs` comment + harness/spikes). **No** contracts assembly, `IExecution`, aliases, or `WorkerNeuron` public surface changes.

Re-confirmed ClientEntryPoint: `Apply` + `Read` only (`IExecution.cs`). Manifest spike still green (`ExecutionManifestUsesDbExecutionAliases`). Prior MEDIUM "worker ledger is public wire" posture unchanged — not a green-b regression.

---

## Demand closure scorecard

| # | Prior demand | Closed? |
|---|--------------|---------|
| 1 | BLOCKER auto-retry with open Dispatched | **Yes** — Attempts gate + Reminders defense; all new-attempt paths traced |
| 2 | Trap-4 settled refusals | **Yes** for demanded handler validation; park race intentionally left |
| 3a | Spike Dispatched+retryable fail | **Yes** — AttemptCount + AcceptCount + Uncertain phase |
| 3b | Spike Completed re-Prepare / no double effect | **Yes** — PrepareCount≥2, ExternalEffectCount==1, AcceptCount≥2 |

---

## Residual notes (not reject criteria for S1.4)

- Non-retryable Dispatched → Failed without Uncertain (reconciliation gap; no auto-retry).
- Reminder defense path not separately spiked (production path is; defense is belt-and-suspenders).
- 3b does not assert Transition-on-Completed refuse (kernel does).
- Prior MEDIUM: public worker-ledger contracts, WorkerNeuron Accept auth, trap-8 catalog load.
- CompleteUserAction intentional non-settled race retained.

---

## Verdict rationale

The sole S14-grill **BLOCKER** is closed on every auto-retry admission path that can mint a new Accept/Attempt. Trap-4 demand conversions landed. Spikes now pin AttemptCount / AcceptCount / ExternalEffectCount on the messy-fail and completed-key paths. Gate 132/132 green. No surface regression in green-b.

VERDICT: APPROVE
