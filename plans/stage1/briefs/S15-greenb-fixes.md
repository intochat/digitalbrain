# S1.5-GREEN-b — close the turn-lifecycle blockers with the terminal bridge   (role: GREEN)

Report path: `plans/stage1/reports/S15-turnsb.md`

Read `plans/stage1/reports/S15-grill.md` fully — REJECT with findings 1–3 hard, 4–6 to take
here, 7–9 allowed to ride. The orchestrator prescribes the architecture below; implement it
(adapt details to the code, but keep the invariants).

## The terminal bridge (fixes findings 1, 3, 5)
Truth lives in the Execution kernel; Chat must never carry a Running turn the kernel has
abandoned, and must never trust an unverified completion push.

1. `StartExecution` already knows its goal; ensure the goal/start record carries the ORIGIN
   neuron id (the chat grain). On every TERMINAL transition (Succeeded/Failed/Cancelled — and
   on entering a durable Blocked state), `ExecutionNeuron` Sends a directed notification fact
   to the origin (generic contract in the Execution module, e.g. `ExecutionTerminal
   {ExecutionId, State, Revision}` — origin-agnostic, NOT chat-aware; mind trap 3 if it's a
   grain call — prefer the Send rail).
2. Chat handles that fact as a WAKE-UP ONLY: it re-Reads the Execution (authoritative) and
   applies the terminal state to the matching ActiveTurn (`ExecutionId` must match); then
   advances the FIFO. A push for an unknown/mismatched ExecutionId is ignored settled. This
   kills the spoofable `CompleteTurnWork` (finding 5) — remove that unbound entry point.
3. **Activation reconcile (finding 1)**: when Chat activates with a durable `ActiveTurnId`, it
   re-Reads the linked Execution and applies its state: terminal → apply + advance; still
   running/blocked → leave Running (the kernel's own reminders drive it to terminal, which
   pushes the bridge fact); execution missing/unreadable → Fail the turn durably + advance.
   The restart test must assert TERMINAL progress after restart (not merely state existence).
4. **Stuck-Running dies (finding 3)**: worker crash → the Execution kernel's attempt/reminder
   machinery reaches a terminal or blocked state on its own (S1.4 guarantees) → bridge fires →
   Chat resolves. Prove with a killed-worker test (worker that dies without completing).

## Cancel semantics (finding 2)
5. `chat.cancel-turn` on the RUNNING head: Chat applies `CancelExecution` to the kernel and
   marks the turn Cancelling — it does NOT clear ActiveTurn and does NOT start the next turn.
   Only the terminal bridge (Cancelled/Failed/Succeeded — a cancel can lose the race to a
   completion; both are honest) advances the queue. The worker must actually observe
   cancellation: wire the S1.4 worker-cancel path through the relay so the AI call's
   CancellationToken fires. Prove: cancel → agent Accept ends without producing a reply
   turn; next queued turn does not Accept until the head is terminal at the kernel.

## Hardening (findings 4, 6)
6. Worker dispatch allow-list: the relay dispatches only to grain types registered as workers
   (explicit registration in module composition — reflection-manifest style, not a string
   switch). A dispatch to an unregistered type refuses settled.
7. Trap 8: the adapter's new `IHandle<>` implementations must not put worker-dispatch facts
   into the broadcast catalog — restructure to directed handling (`OnUnbound`/directed Send
   pattern like the kernel uses) or justify precisely in the report why the ghost cost is
   accepted (measure: which facts, how often emitted).

## Constraints
TDD every fix first. Full gate. No new packages. No git. Findings 7–9 may remain — list them
as riding. ChartVocabularyProofs single timeout = known ticketed flake (rerun clean).

## Definition of done
Gate green; tests prove: restart mid-turn reaches terminal; killed worker reaches Failed and
queue advances; cancel stops work and never double-runs the queue; unregistered worker type
refused; no spoofable completion path; broadcast catalog unchanged (or measured+justified).
Report maps finding → fix → test.
