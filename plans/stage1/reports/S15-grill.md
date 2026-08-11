# S1.5-GRILL — adversarial review of durable conversation turns

**Subject:** `6d00b139` S1.5 durable turns (chat turn = Execution)  
**Role:** GRILL (judge only; no production code or git writes)  
**Worker report:** `plans/stage1/reports/S15-turns.md`  
**Brief:** `plans/stage1/briefs/S15-turns.md`  
**Authority:** `GROK.md`, kernel traps, S1.5 brief DoD, S1.4 OutcomeUncertain invariant

---

## Gate (verified this session)

```
dotnet build DigitalBrain.slnx
  Build succeeded.
  0 Error(s)
  (AppHost node NO_COLOR/FORCE_COLOR noise only — not C# / TreatWarningsAsErrors)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
  Total: 142, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: ~82s
```

ChartVocabularyProofs did not flake this run. Suite is green. Gate alone does **not** authorize APPROVE.

---

## Attack results

### (1) Kernel traps

| Trap | Result | Notes |
|------|--------|-------|
| **1** — await own Send without `FlushOutboxAsync` | **PASS (narrow)** | Chat pipeline does not await delivery *effects* of its own Emit/Send. `SendAsync` only journals + outbox (`Neuron.Messaging.cs:8–126`). No arm-before-offer style wait on `TurnLifecycle`. Residual: `ChatTurnWorker.Accept` holds a single grain turn across the full AI stream, so mid-Accept `SendAsync(AttemptAccepted)` is **not drained until Accept ends** — lag, not deadlock-by-starvation. |
| **2** — zero-receiver silent loss | **PASS for product surface** | `TurnLifecycle` (`chat.turn-lifecycle`) has no `IHandle<>`. Emit still **journals** outgoing; consumers are journal observers (`MapChatStreams.cs:124–133`, `MapOwnerCommands.cs:212–217`). Route confirmation is journal-read, not graph delivery. Visible SSE/events do not depend on outbox receivers. |
| **8** — new `IHandle<T>` ghosts | **MAJOR** | `ChatTurnWorker` declares `IHandle<DispatchWorkerAccept|Continue|Cancel>` (`ChatTurnWorker.cs:17–19`). Same synapses already catalogued via `WorkerNeuron`; this adds a **second** broadcast handler grain type (`chat-turn-worker`). Directed relay uses `SendAsync`, so ghosts only fire if something **Emits** those types — still catalog pollution and trap-8 debt for the first production adapter. `CompleteTurnWork` correctly uses `OnUnboundSynapseAsync` (no new broadcast T). |

No trap-1 starvation found in chat enqueue / cancel / complete paths. Trap-4 actor refusals use `NeuronAuthorizationException` (settled) — see (7).

---

### (2) FIFO integrity

| Scenario | Result | Evidence |
|----------|--------|----------|
| Two rapid Sends | **PASS** | Same chat grain is single-threaded; `EnqueueTurnAsync` + `TryStartNextAsync` serializes. Second turn stays `Pending` while `ActiveTurnId` set (`Chat.cs:331–337`, `374–388`). Proven: `FifoQueueRunsTurnsInArrivalOrderWithinOneConversation`. |
| Concurrent conversations | **PASS** | Distinct chat grains / workers. Proven: `DifferentConversationsRunConcurrently`. |
| CommandId dedupe | **PASS** | `IsUnseenCommand` + retained turn return (`Chat.cs:284–297`, `405–430`). |
| Cancel queued advances | **PASS** | Removes pending id, emits lifecycle, `TryStartNextAsync` (`Chat.cs:89–103`). Proven: `CancelQueuedTurnAdvancesTheQueue`. |
| Cancel-of-head vs completion | **BLOCKER** | See (2b) and (3). |
| Queue / Running head across restart | **BLOCKER** | Durable state survives; **no activation recovery**. See (2a). |

#### (2a) BLOCKER — Running head freezes the queue after silo restart

`TryStartNextAsync` returns immediately when `ActiveTurnId is not null` (`Chat.cs:334–337`). There is **no** `OnNeuronActivatedAsync` / reminder path that re-drives a durable `Running` head or clears a dead `ActiveTurnId`.

After restart mid-AI:

- Chat: `Running` + `ActiveTurnId` + execution name still durable.
- Execution: if Accept already ran, typically `Running`/`Pending` with **no** pending `AcceptWorkerDispatch` and **no** retry (policy `MaximumAttempts: 1`, `Chat.cs:25–28`).
- Worker: in-memory attempt CTS / hold is gone; **nobody re-Accepts**.

Result: turn never Completes/Fails; queue never advances; conversation is stuck forever.

`RunningTurnSurvivesSiloRestartAndCompletes` (`DurableTurnProofs.cs:365–451`) only proves **non-vanishing**. The final wait accepts **any** status including `Running`/`Pending` (`:441`) — it does **not** prove completion after restart. Brief DoD item 6 ("completes or fails durably after restart") is **not** met; worker report risk #3 admits the soft landing.

#### (2b) BLOCKER — Cancel of running does not interrupt Accept; FIFO one-active is violated in the race window

`Chat.Cancel` for a Running turn (`Chat.cs:106–137`):

1. `execution.Apply(CancelExecution)` — stages `CancelWorkerDispatch`.
2. **Immediately** marks chat turn `Cancelled`, clears `ActiveTurnId`, emits lifecycle, **`TryStartNextAsync`** (next Execution may start).

`DispatchWorkerCancel` targets the **same** `chat-turn-worker` grain that is still inside `Accept` (`ChatTurnWorker.cs:50–118`). Orleans serializes grain messages: **Cancel cannot run until Accept returns**. `_attemptCts.Cancel()` (`:124`) therefore cannot stop the in-flight AI.

Consequences:

- Cancelled head's AI continues until Accept ends.
- Next turn can start while the previous Accept still runs → **two concurrent AI streams on one conversation** (violates ratified "ONE active Execution per conversation" at the work-doing layer).
- If Accept finishes **successfully** after chat already cancelled: `CompleteTurnAsync` is idempotent on terminal chat status (skips `Responded`) — good for transcript — but Execution may still take `AttemptSucceeded` while `Cancelling` (`ExecutionNeuron.Attempts.cs:86–91` does **not** treat `Cancelling` as ignore) → Execution `Succeeded` after owner cancel.

`CancelRunningTurnIsVersionedAndIdempotent` only waits for **chat** status `Cancelled` (eager write), never asserts Execution terminal Cancelled or that the agent stopped Accepting.

---

### (3) Execution adapter — OutcomeUncertain & durable Failed

| Check | Result |
|-------|--------|
| Honor OutcomeUncertain for non-idempotent AI | **REJECT as decision** | Worker never emits `AttemptOutcomeUncertain`. Failures are `AttemptFailed(..., Retryable: false)` (`ChatTurnWorker.cs:233–241`). Policy is single-attempt, non-retryable. That **avoids** auto-retry double-spend of model/tool work (aligned with S1.4 spirit for non-idempotent work) **but** only covers exceptions inside `Accept`. |
| Worker process death / silo loss mid-Accept | **BLOCKER** | No `catch` path runs → no `CompleteTurnWork` → chat stays `Running` with `ActiveTurnId` → queue freeze (same as 2a). Execution may remain non-terminal; chat does **not** subscribe to Execution terminal facts. |
| Exception inside Accept → durable Failed | **PASS** | `catch (Exception)` → `FinishAsync(..., Failed)` + `AttemptFailed` (`:108–117`). |
| Empty answer | **NOTE** | Treated as `Completed` with detail `empty-answer` (`:74–84`) — not Failed. |

**Verdict on adapter decision:** Terminal non-retryable fail for *caught* failures is defensible. Claiming the adapter honors OutcomeUncertain is false; claiming "worker failure never leaves stuck Running" is false for crash/restart. Product DoD fails.

---

### (4) WorkerDispatchRelayNeuron — arbitrary grain type

**Prior pin (S1.4):** `ValidateWorker` required grain type = `IWorker` / `"worker"`.

**Now (`WorkerDispatchRelayNeuron.cs:55–73`):** owner match + non-empty type/name only. Comment admits domain adapters.

| Risk | Severity |
|------|----------|
| Any same-owner grain id can be named as `Worker` on `StartExecution` and receive `DispatchWorkerAccept` | **HIGH** (typing/security hole vs prior pin) |
| Mitigations: owner-bound; worker still must handle the dispatch synapse or fail; Execution Start is not a public HTTP owner command | reduces residual blast radius |
| Required for `chat-turn-worker` without hijacking harness `IWorker` grain type | design cost, not free |

**Judgment:** Not an automatic REJECT alone if allow-list of worker types is deferred with explicit threat model — but combined with unauthenticated `CompleteTurnWork` (below) it widens the attack surface. Prefer allow-list (`worker`, `chat-turn-worker`) or capability registration over open type.

**Related:** `CompleteTurnWork` is accepted on Chat via `OnUnboundSynapseAsync` with **no caller identity check** (`Chat.cs:197–203`). Any party that can `Send` to the chat can force terminal statuses / inject `Responded` text. **HIGH** forgery hole for the completion path.

---

### (5) Observer semantics

| Check | Result |
|-------|--------|
| P0-2 abort detaches | **PASS** | `MapOwnerCommands.StreamDeltasAsync` durable `Send` then `WatchJournalAsync` with linked `requestAborted` + budget (`:177–219`). No `CreateLinkedTokenSource(requestAborted)` on the AI run. Composition + behavior proofs present. |
| Watch disposal on abort | **PASS** | Client `WatchJournalAsync` finally `TeardownWatchAsync` (`DigitalBrainClient.cs:145–148`). |
| Unbounded journal watch | **MEDIUM** | POST observer always starts `afterSequence: 0` (`MapOwnerCommands.cs:199`) and scans until matching `Responded` / terminal lifecycle. Bounded by `TurnBudget`, not by cursor from accept. Heavy chats pay full journal replay per send. Resume surface `/chats/{name}/events` correctly takes `afterSequence`. |
| Reconnect reconstructs queued/running/completed from events | **PASS with gap** | Additive `TurnLifecycle` projection supplies `TurnId` + `Status` (`MapChatStreams.cs:124–133`, `HttpSurfaceModels.cs:32–33`). `UserMessaged` / `Responded` still carry transcript text. **Gap:** reconnect that only watches events cannot rebuild full `ReadTurns` snapshots if lifecycle facts fell out of journal retention while durable turn-log still holds them — dual source of truth. Acceptable if clients treat events as live + use ReadTurns for authority (ReadTurns is grain API, not SSE). |
| SendStreaming observer-empty | **NOTE** | Grain stream yields nothing after enqueue (`Chat.cs:54–63`); HTTP POST path does not use it for deltas. Token SSE mid-turn not re-published (worker report risk #2). Matches brief resume surface, not full live token fanout. |

---

### (6) P0-6 safe-point persistence

| Check | Result |
|-------|--------|
| Persist after tool rounds | **PASS (code)** | `DirectAgentSession.RunStreamingAsync` persists on `FunctionResultContent` and again after stream (`DirectAgentSession.cs:58–77`, `80–81`). |
| Persist only composition-tested | **MEDIUM** | `DirectAgentSessionPersistsAtToolRoundSafePoints` is source-shape only (`DurableTurnProofs.cs:34–69`) — no cluster proof that restore skips re-invocation. |
| Crash between tool effect and Persist | **MEDIUM (inherent residual)** | Safe point is **after** function result content is observed. Crash in the window tool-ran → pre-persist still allows **double-execute on resume** if MAF re-drives the incomplete session. Brief asked for safe points "at minimum after each completed model/tool round" — met for the persist call site; residual at-least-once remains. Model-only rounds still only final-persist. |

---

### (7) Actor refusal + scripting stamps

| Check | Result |
|-------|--------|
| Grain refuses actorless Send | **PASS** | `RequireActor` → `NeuronAuthorizationException` (`Chat.cs:486–498`). Proven: `ActorlessSendIsRefusedAtTheGrain`. |
| Cancel requires actor | **PASS** | `Cancel` calls `RequireActor` (`:68`). |
| Scripting sample stamped | **PASS** | `chat-probe.cs:14–18`. |
| MCP ChatTools stamped | **PASS** | `ChatTools.cs:47–50`. |

---

### (8) Contract hygiene

| Check | Result |
|-------|--------|
| Additive event fields | **PASS** | `ChatTurnEvent.TurnId` / `Status` optional (`HttpSurfaceModels.cs:32–33`). Older clients ignore. |
| Wire aliases | **PASS** | New aliases under `chat.*` (`chat.turn-lifecycle`, `chat.cancel-turn`, `chat.turn-accepted`, …). No rename of existing `chat.user-messaged` / `chat.responded`. |
| MAF types outside AI | **PASS** | No `Microsoft.Agents` under UI. UI refs Execution + AI **contracts**; MAF session stays in `DirectAgentSession` (AI module). |
| UI → Execution **implementation** project ref | **NOTE** | `DigitalBrain.Modules.UI.csproj` refs Execution impl so the silo loads `ChatTurnWorker` — acceptable for first adapter; watch module boundary creep. |

---

## Test honesty

| Claimed behavior | Test | Honest? |
|------------------|------|---------|
| Observer abort ≠ AI cancel | `SendReturnsTurnIdAndCompletes…` | **Yes** |
| FIFO order | `FifoQueueRunsTurns…` | **Yes** (hold serializes Accept) |
| Concurrent chats | `DifferentConversations…` | **Yes** |
| Cancel queued | `CancelQueuedTurn…` | **Yes** |
| Cancel running versioned/idempotent | `CancelRunningTurn…` | **Partial** — chat status only; not Execution/worker stop |
| Restart mid-turn completes | `RunningTurnSurvives…` | **No** — non-vanish only; any status wins |
| P0-6 safe points | composition source scan | **Partial** — no restore/double-exec proof |
| Chat uses Execution | composition source scan | **Yes** as wiring pin |

PIN-DEFECT markers for P0-2/P0-6: **removed** (grep clean except comments). Flipped in name only where restart/cancel depth is soft.

---

## What is solid (do not re-litigate as blockers)

- Durable turn log + queue state + `TurnAccepted` receipt.
- P0-2: request abort detaches observer; AI path not linked to request CT.
- Actor stamp at grain + operator samples.
- Additive SSE lifecycle projection.
- Single-attempt non-retryable AI failure path (when exceptions are caught).
- Gate green 142/142 this session.

---

## Findings (file:line · severity)

1. **`Chat.cs:331–396` · BLOCKER** — No activation/reminder recovery for durable `Running` + `ActiveTurnId`. Silo restart mid-turn freezes the conversation queue; brief DoD "completes or fails after restart" unmet. Weak test: `DurableTurnProofs.cs:437–441`.
2. **`Chat.cs:106–137` + `ChatTurnWorker.cs:50–133` · BLOCKER** — Cancel of running clears active and starts the next turn before the worker can process cancel; same-grain Accept serializes Cancel behind the AI run → cancel does not stop work; concurrent Accepts possible; Execution may `Succeeded` after owner cancel (`ExecutionNeuron.Attempts.cs:86–91` ignores `Cancelling` incompletely).
3. **`ChatTurnWorker.cs:108–117` vs crash · BLOCKER** — Caught exceptions → Failed; process/silo death mid-Accept never emits `CompleteTurnWork` → stuck `Running` (no Execution→Chat terminal bridge).
4. **`WorkerDispatchRelayNeuron.cs:55–73` · HIGH** — Arbitrary same-owner grain type dispatch (removed `IWorker` type pin).
5. **`Chat.cs:197–203` · HIGH** — `CompleteTurnWork` accepted unbound without verifying worker/execution caller → spoofable terminal status / transcript injection.
6. **`ChatTurnWorker.cs:17–19` · MAJOR (trap 8)** — New `IHandle<DispatchWorker*>` on production adapter expands broadcast catalog ghosts.
7. **`MapOwnerCommands.cs:199` · MEDIUM** — POST observer always `afterSequence: 0` (full journal scan per send; budget-bounded).
8. **`DirectAgentSession.cs:65–70` · MEDIUM** — Tool safe-point after result content; residual double-execute on crash before persist; only composition test for P0-6.
9. **`ChatTurnWorker.cs:64–67` · LOW** — `AttemptAccepted` outbox not drained until Accept ends (Execution state lags entire AI call).

---

## Required before APPROVE

1. **Restart / crash recovery for the queue head** — on Chat (and/or Execution) activation: if `ActiveTurnId` is set, re-drive or reconcile with Execution terminal state; if Execution is dead/non-progressing past a policy deadline, mark turn `Failed` and `TryStartNextAsync`. Prove with a test that asserts **terminal** status after restart (not "still exists").
2. **Cancel of running must stop work without starting the next turn until the head is terminal at the worker** — or use a cancel channel that is not serialized behind Accept (durable cancel flag polled by worker, separate cancel grain, etc.). Prove agent Accept ends without success side effects and next turn does not Accept until then.
3. **Close stuck-Running on worker death** — chat observes Execution terminal / OutcomeUncertain / deadline, or Execution emits a fact Chat handles, so Failed is durable and the queue advances.
4. **Hardening (can be same PR or immediate follow):** allow-list worker types **or** document + test threat model; authenticate `CompleteTurnWork` (caller = assigned worker / matching Execution).

Items 4–9 may ride as MAJORs after 1–3 if explicitly accepted in the re-grill brief; **1–3 are hard REJECT**.

---

## VERDICT: REJECT
