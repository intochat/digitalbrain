# S1.5-GRILL-2 — re-grill of terminal-bridge fix

**Subject:** `fe20a8dd` S1.5-GREEN-b (Execution→origin terminal bridge)  
**Prior:** `6d00b139` S1.5 durable turns · grill `S15-grill.md` (REJECT) · green report `S15-turnsb.md`  
**Role:** GRILL-2 (judge only; no production edits; no git writes)  
**Authority:** `GROK.md`, kernel traps, `S15-greenb-fixes.md` DoD, S1.5 brief DoD item 6, S1.4 OutcomeUncertain

---

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  2 Warning(s) — AppHost node NO_COLOR/FORCE_COLOR noise only (not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 147, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~116s
```

ChartVocabularyProofs did not flake this run. Gate green does **not** authorize APPROVE alone.

---

## What changed since REJECT (scope check)

| Area | Claimed | Diff reality |
|------|---------|--------------|
| Terminal bridge | `ExecutionTerminal` directed wake-up; CompleteTurnWork removed | Landed |
| Activation reconcile | `OnNeuronActivatedAsync` → `ReconcileActiveExecutionAsync` | Landed |
| Cancel head | Cancelling; no clear Active / no TryStartNext until terminal | Landed |
| Worker fire-and-return | `BeginAccept` returns immediately; Cancel can interleave | Landed |
| Liveness | Reminder after Accept + `FailAbandonedRunningIfNeededAsync` | Landed in code |
| Allow-list | `WorkerGrainTypeRegistry` + settled refuse | Landed |
| Trap 8 | ChatTurnWorker `OnUnbound` only | Landed |
| Tests | 142 → 147; restart/cancel strengthened; kill + allow-list + origin notify added | Strengthened, not weakened |

---

## Attack results

### (1) Restart mid-turn — `RunningTurnSurvivesSiloRestartAndCompletes`

| Check | Result | Evidence |
|-------|--------|----------|
| Test asserts **terminal** head (not existence) | **PASS** | `DurableTurnProofs.cs:442–464` — `Completed \| Failed \| Cancelled` only |
| Test asserts queue progress | **PARTIAL** | `:466–478` — next may be terminal **or still Running**; does not require next Completes |
| Activation reconcile re-Reads Execution | **PASS** | `Chat.cs:323–329` |
| Terminal on Read → apply + advance | **PASS** | `Chat.cs:352–356` → `ApplyExecutionSnapshotToTurnAsync` |
| Missing/unreadable Execution → Fail + advance | **PASS** | `Chat.cs:331–349` |
| Still Running/Pending/Cancelling after restart | **PASS (product path)** | Cancel via kernel (`Chat.cs:362–371`), then re-Read; if still non-terminal mark Cancelling (`:394–402`) |
| DelayDeactivation interplay | **PASS intent** | Active head `DelayDeactivation(2h)` (`Chat.cs:630`) so idle reactivation does not spuriously cancel a **live** AI; after silo restart Delay is gone → reconcile runs (correct) |
| Execution Waiting forever | **HOLE** | Reconcile only drives `Running \| Pending \| Cancelling` (`Chat.cs:362`). **`Waiting` is a no-op** — ActiveTurnId stays set forever. Bridge ignores Waiting (`Chat.cs:244–247`). No chat-side resolve path. |

**Reconcile path after restart (intended):**

```
Chat activates → ReconcileActiveExecutionAsync
  → Read Execution
  → if Running/Pending/Cancelling: CancelExecution
  → if kernel reaches terminal (AttemptCancelled / ActiveAttempt null): apply + TryStartNext
  → else leave Cancelling; Execution RecoverAfterActivation re-stages CancelWorkerDispatch
```

**Does anything resolve Execution stuck in Waiting?** Kernel reminders never auto-exit `OutcomeUncertain` (S1.4). Chat will not Cancel Waiting on activate. **Queue freezes** until external `ResolveOperation` (not exposed on the chat surface).

**Judgment on blocker 1:** Functional freeze-after-restart is **closed** for the common Running head (cancel-unstick). Test honesty improved (terminal, not non-vanish). Residual: Waiting park freezes FIFO; restart “completes” often as **Cancelled**, not Completed (admitted in green report) — acceptable for DoD “completes or fails” if Cancelled counts as durable terminal progress. **Not a hard reopen of blocker 1**, but Waiting is a related freeze class.

---

### (2) Cancel-of-running — interleaving & races

| Check | Result | Evidence |
|-------|--------|----------|
| Head stays active until terminal | **PASS** | `Chat.cs:118–143` — Cancelling only; no Active clear; no TryStartNext |
| BeginAccept fire-and-return | **PASS** | `ChatTurnWorker.cs:43–59` — returns; AI on background task |
| Cancel can interleave / CTS fires | **PASS** | `Cancel` (`:130–141`) cancels CTS + always `AttemptCancelled` |
| Next turn Accept before head terminal | **PASS (proof)** | `CancelRunningTurnIsVersionedAndIdempotent` keeps next `Pending` and AcceptCount flat until Cancelled (`DurableTurnProofs.cs:371–380`) |
| Agent cancelled | **PASS** | `WasCancelled("cancel-run")` (`:383`) |
| One reply only | **PASS (this scenario)** | assistant reply count == 1 for queued turn (`:393–394`) |

#### Race both ways (cancel vs completion)

**Execution `AttemptSucceeded` while `Cancelling` is accepted** (`ExecutionNeuron.Attempts.cs:90–92` excludes only Succeeded/Failed/Cancelled, **not** Cancelling). So:

| Winner | Path | Honest? |
|--------|------|---------|
| Cancel first | `AttemptCancelled` while Cancelling → Cancelled → bridge; later Succeeded ignored (terminal) | Yes |
| Success first | Succeeded (even from Cancelling) → bridge Completes + Responded; later Cancelled ignored | Yes (cancel loses) |

Double-reply under this race: Execution admits **one** terminal; Chat applies once from Running/Cancelling → terminal. **Queue advances once** (ActiveTurnId cleared then TryStartNext; Orleans serializes Chat deliveries). **PASS** for “exactly one queue advance / no double-run of next Accept” in the cancel test.

#### Replay / second terminal apply

`ApplyExecutionSnapshotToTurnAsync` already-terminal path (`Chat.cs:421–437`) still calls `TryEmitRespondedAsync`, which **always `EmitAsync(Responded)`** before transcript text-dedupe (`:493–514`). Delivery dedupe is by **SynapseId** only (`Neuron.Turns.cs:12–13`), not by ExecutionId+Revision. A **second** `ExecutionTerminal` (new SynapseId, same Execution) re-emits Responded. **Not** idempotent by ExecutionId+Revision. Severity: **MAJOR** (journal/SSE duplication), not FIFO double-start.

#### Cancelling deadlock if execution never terminalizes?

| Scenario | Recovery? |
|----------|-----------|
| Cancel dispatched, worker acks AttemptCancelled | Terminal Cancelled — OK |
| Silo restart while Cancelling | `RecoverAfterActivationAsync` re-stages Cancel (`ExecutionNeuron.Dispatch.cs:144–150`) |
| Cancelling + PendingDispatch null + no AttemptCancelled (worker died after dispatch cleared, before ack) | **`FailAbandonedRunningIfNeededAsync` requires `State == Running` only** (`Dispatch.cs:168–171`) — **does not fail Cancelling**. Stuck until chat re-activates and re-Cancel, or another Cancel path. **MAJOR residual** |
| Background `RunAcceptAsync` concurrent with grain methods | Fire-and-return mutates worker fields + `SendAsync` with `_handling is null` (`Neuron.Messaging.cs:105–124`) outside Orleans turn serialization. Cancel test green; concurrency hazard remains **MAJOR** residual |

**Judgment on blocker 2:** Core FIFO/cancel semantics **closed** under test. Residual deadlock window on Cancelling without FailAbandoned coverage; Responded replay not revision-idempotent.

---

### (3) Worker death — liveness reminder + FailAbandoned

| Check | Result | Evidence |
|-------|--------|----------|
| Abandonment deadline | **15s due**, period **1 min** | Registered on `AttemptAccepted` (`Attempts.cs:21–22`); `ReminderPeriod = 1m` (`ExecutionNeuron.cs:34`) |
| Durable? | **Orleans reminder** (production durable); test cluster uses **in-memory** reminders | Green report admits in-memory does not survive restart |
| Fires after full silo restart? | **Depends** | Durable reminder service should retain registration. `RecoverAfterActivationAsync` does **not** re-register liveness for bare Running without PendingDispatch (`Dispatch.cs:134–161`) — if reminder is lost, only Chat cancel-on-activate recovers |
| FailAbandoned action | Running + no PendingDispatch → Failed `WorkerAbandoned` **or** Waiting `OutcomeUncertain` if Dispatched ops | `Dispatch.cs:165–203` |
| Notify origin | Yes on both branches | `NotifyOriginOfStateAsync` |
| Chat on Failed | Advances FIFO | bridge / reconcile |
| Chat on Waiting (OutcomeUncertain) | **Does not advance** | `Chat.cs:244–247` ignore Waiting; reconcile does not Cancel Waiting |

#### Does `KilledWorkerReachesFailedAndQueueAdvances` really kill?

**No pure worker kill.** Test (`DurableTurnProofs.cs:488–524`): hold agent → **`RestartSilosAsync`** → expect head `Failed | Cancelled` → next Completes.

That exercises **Chat restart reconcile + CancelExecution**, not:

- process death of worker alone with silo up, nor
- the 15s `FailAbandonedRunningIfNeededAsync` path, nor
- `WorkerAbandoned` failure type specifically (Cancelled from cancel-unstick also passes).

So the **named** killed-worker proof is a restart-recovery proof. Liveness abandonment is **code-only** for non-restart death.

#### Critical freeze if abandonment parks OutcomeUncertain

If the attempt had any `OperationPhase.Dispatched` rows, FailAbandoned parks **Waiting / OutcomeUncertain** and notifies origin. Chat **ignores Waiting** and never cancels it on activate → **conversation FIFO frozen forever** with ActiveTurnId set. Chat AI tool rounds that touch the operation ledger make this product-real (P0-6 surface). Simple scripted hold without ops fails cleanly to WorkerAbandoned — tests only cover the simple path.

**Judgment on blocker 3:** Closed for restart-mid-hold (via cancel) and for clean Running abandonment without Dispatched ops (code). **Not closed** for abandonment → OutcomeUncertain → Chat freeze. Kill test does not prove liveness/FailAbandoned.

---

### (4) Bridge — trap 2, replay, forgery

| Check | Result | Evidence |
|-------|--------|----------|
| Directed Send (not broadcast Emit) | **PASS** | `NotifyOriginOfStateAsync` → `SendAsync(origin, ExecutionTerminal)` (`Support.cs:143–145`) |
| Trap 2 zero-receiver | **N/A for directed** | Receiver is Origin NeuronId; always targeted. Lost delivery = outbox retry, not “no receivers” |
| Origin gone / notification lost | **PARTIAL recovery** | Activation reconcile re-Reads Execution and applies terminal **or** Cancel-unstick. While Chat stays warm under `DelayDeactivation(2h)`, lost notify waits on Execution re-notify (FailAbandoned / terminal Attempt*) or eventual deactivation |
| Replay idempotent by ExecutionId+Revision? | **FAIL** | No revision monotonicity check. Status apply is one-shot to terminal; **Responded re-emits** on already-terminal path without revision gate |
| Re-Read for authority (brief + green-b DoD) | **FAIL** | `ReconcileFromExecutionTerminalAsync` **builds snapshot from payload** and applies Result/Failure without Read (`Chat.cs:272–289`). Comment admits deliberate non-Read to avoid deadlock |
| Forged ExecutionTerminal tricks chat? | **YES — FAIL** | Any same-owner party that can `Send`/`Deliver` `ExecutionTerminal` matching an active/known `ExecutionName` injects terminal status + `ChatTurnResult` text via `TryEmitRespondedAsync`. **No caller identity check** (contrast Execution’s AttemptFact caller==worker filter at `ExecutionNeuron.cs:68–70`). Owner match only (`Chat.cs:239–241`) |

**Brief architecture (S15-greenb-fixes.md §2):** wake-up only → **re-Read Execution (authoritative)**.  
**Implementation:** payload is authority for State/Result/Failure. Green report claims “never trusts free-form completion push” while trusting a free-form `ExecutionTerminal` body — **finding 5 renamed, not closed**.

Contract comment on `ExecutionTerminal.cs:6–7` still says origin re-Reads; Chat does not.

**Judgment:** **BLOCKER-class reopen of finding 5** (spoofable transcript/terminal). Activation reconcile is the honest re-Read path but is not what the live bridge uses.

---

### (5) Allow-list — registry composition

| Check | Result | Evidence |
|-------|--------|----------|
| Composition registration | **PASS** | `ExecutionModule` seeds `worker` + `chat-turn-worker`; `UiModule` also registers chat-turn-worker; registry built once from `IWorkerTypeRegistration` (`ExecutionModule.cs:16–34`) |
| Runtime arbitrary register by hostile client? | **No** | No owner command mutates registry. Modules present at silo compose can `AddSingleton<IWorkerTypeRegistration>` for any string type name (trusted composition, not hot path) |
| `Allow()` on live singleton | **Residual** | Public `WorkerGrainTypeRegistry.Allow` — only code holding the singleton can expand; not owner HTTP |
| Unregistered refuse settled? | **PASS** | `NeuronAuthorizationException("worker-type-not-registered:…")` (`WorkerDispatchRelayNeuron.cs:71–76`) |
| Start still accepts bad type | **NOTE** | Start does not validate type; dispatch retries via reminder; Execution may stay **Pending** (`UnregisteredWorkerTypeIsRefusedByDispatchRelay` allows Pending or Failed — does not require Failed) |

**Judgment:** Finding 4 **closed** for settled refusal + composition allow-list. Residual stuck-Pending on forever-refused dispatch is **MEDIUM**.

---

### (6) Regression sweep — tests weakened?

`git diff HEAD~1 -- src/Tests/**` (2 files, +182/−36):

| Prior proof | Change | Weakened? |
|-------------|--------|-----------|
| `RunningTurnSurvives…` accepted Pending/Running/Completed; asserted NotEqual Failed | Now requires terminal; queue progress | **Strengthened** |
| `CancelRunningTurn…` chat Cancelled only | Cancelling, AcceptCount, WasCancelled, one reply | **Strengthened** |
| CompleteTurnWork / IHandle pins | Removed Completeturn; directed-only composition | **Tightened** |
| New kill / allow-list / origin notify | Added | Net new coverage |
| PIN-DEFECT for this work | None introduced | Clean |

No prior behavior proof deleted solely to go green. Soft restart pin replaced by harder terminal assert (status may be Cancelled via cancel-unstick — honest).

---

## Blocker scorecard vs S15-grill hard items

| # | Original hard blocker | Status after GREEN-b |
|---|----------------------|----------------------|
| **1** | Restart freezes Running queue | **CLOSED** (cancel-unstick + terminal test). Residual Waiting freeze |
| **2** | Cancel clears head / concurrent Accepts / cancel doesn’t stop work | **CLOSED** under cancel test. Residual Cancelling-without-FailAbandoned; background-task concurrency |
| **3** | Worker death stuck Running | **PARTIAL** — restart path closed; liveness untested; **OutcomeUncertain park freezes Chat** |
| **4** | Arbitrary worker type | **CLOSED** (allow-list + settled) |
| **5** | Spoofable CompleteTurnWork | **REOPENED as ExecutionTerminal trust-without-Read / no caller check** |
| **6** | Trap 8 IHandle ghosts on adapter | **CLOSED** (OnUnbound) |

Riding from prior grill (unchanged): POST `afterSequence: 0` MEDIUM; P0-6 composition-only MEDIUM; AttemptAccepted lag reduced by fire-and-return.

---

## Findings (file:line · severity)

1. **`Chat.cs:272–289` + `Chat.cs:493–507` · BLOCKER** — Live bridge applies `ExecutionTerminal` State/Result/Failure without re-Reading Execution and without verifying `delivery.Caller == ExecutionId`. Hostile same-owner Send forges terminal status and injects `Responded` text. Violates green-b brief §2 and attack criterion (4). Finding 5 not closed.
2. **`Chat.cs:244–247` + `Chat.cs:362` + `ExecutionNeuron.Dispatch.cs:176–189` · BLOCKER** — Worker abandonment that parks `OutcomeUncertain` (Dispatched ops present) notifies Waiting; Chat ignores Waiting and never Cancel-reconciles Waiting → durable FIFO freeze with ActiveTurnId set. Product-relevant for tool rounds.
3. **`ExecutionNeuron.Dispatch.cs:168–171` · MAJOR** — `FailAbandonedRunningIfNeededAsync` only for `Running`. Cancelling with PendingDispatch cleared and no AttemptCancelled can stick until another Cancel/re-activate path.
4. **`Chat.cs:421–437` + `Chat.cs:493–507` · MAJOR** — Re-apply not idempotent by ExecutionId+Revision; already-terminal path re-Emits `Responded` (SynapseId dedupe only).
5. **`ChatTurnWorker.cs:58` + `Neuron.Messaging.cs:105–124` · MAJOR** — Fire-and-return Accept runs AI/`SendAsync` outside the grain turn; races grain deliveries on durable journals/outbox. Tests green; model remains concurrent-by-construction.
6. **`DurableTurnProofs.cs:488–524` · MAJOR (test honesty)** — `KilledWorkerReachesFailedAndQueueAdvances` restarts silos (cancel recovery), does not prove 15s liveness / `WorkerAbandoned` / pure worker death.
7. **`DurableTurnProofs.cs:466–478` · LOW** — Restart queue progress accepts next still `Running` (partial DoD).
8. **`WorkerDispatchRelayNeuron.cs:71–76` + Start path · MEDIUM** — Unregistered type refuses dispatch settled but Start leaves Pending with reminder retries; test allows non-Failed.
9. **`ExecutionNeuron.Attempts.cs:90–92` · NOTE (accepted)** — Succeeded while Cancelling is allowed; honest cancel-vs-complete race per brief.

---

## What is solid (do not re-litigate)

- Directed `ExecutionTerminal` from kernel on terminal transitions; CompleteTurnWork removed.
- Cancel of running keeps FIFO head until terminal; cancel test proves no early second Accept and agent cancel.
- Restart test now asserts **terminal** head (not non-vanish).
- Allow-list composition + settled `worker-type-not-registered`.
- ChatTurnWorker directed-only (trap 8 closed for the adapter).
- Actor refusal, FIFO proofs, P0-2 observer detach remain.
- Gate 147/147 this session; no test suite weakened to pass.

---

## Required before APPROVE

1. **Close bridge authority (finding 1):** On `ExecutionTerminal`, treat as wake-up only: re-Read Execution (or verify caller is the Execution grain **and** State/Revision/Result match a Read). Never apply free-form Result text without kernel confirmation. Prove forgery refused / ignored.
2. **Close abandonment → Chat freeze (finding 2):** If Execution is Waiting/OutcomeUncertain with ActiveTurn still head, either advance via durable Failed after policy, surface resolve, or cancel-unstick on activate for abandoned AI turns — must not freeze FIFO forever without an owner path.
3. **Prove pure worker death / liveness (finding 6):** Test that does not restart the whole silo (or explicitly drives the reminder) and asserts Failed/`WorkerAbandoned` + queue advance.
4. **(Strongly preferred same PR)** Revision/ExecutionId idempotent re-apply; FailAbandoned or equivalent for stuck Cancelling; optional: refuse Succeeded while Cancelling if product wants cancel-wins always (brief currently allows honest race).

Items 3–5 MAJOR residuals may ride only after 1–2 are closed.

---

## VERDICT: REJECT
