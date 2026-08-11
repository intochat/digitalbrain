# S1.5 — durable conversation turns on the Execution kernel   (role: RED+GREEN)

Report path: `plans/stage1/reports/S15-turns.md`

## Ratified constraints (binding)
- **P0-2**: a dropped browser connection must NEVER decide whether the brain finishes its work.
  POST appends the user message + a durable TurnId and the AI run proceeds independently;
  request-abort only detaches the observer. Explicit cancel = a versioned command.
- **FIFO (ratified)**: ONE active Execution per conversation; additional turns are durably
  queued in arrival order; cancel advances the queue; different conversations run concurrently.
- **Execution is the runner** (S1.4 module — Conversation is its FIRST production adapter).
  Do NOT invent a parallel job mechanism (banned: second job framework). The queue/turn state
  lives in the chat's durable state; each running turn is one Execution.
- **P0-6**: the MAF agent session must persist at safe points (at minimum after each completed
  model/tool round and at completion), not only after the whole stream completes; fingerprint
  drift keeps its explicit reset/migration path.
- **Scope guard**: the full Conversation-module extraction (D1–D6) is Stage 2. You evolve the
  EXISTING Chat neuron into the durable turn pipeline; you do not create a new module.
- **Actor enforcement at the grain** (S1.2-GRILL MAJOR-2): Chat refuses a durable owner command
  without an Actor stamp — settled refusal (trap 4). Update any Scripting samples that fire
  SendMessage to stamp an operator actor.

## Method — characterize first, then replace
RED phase (inside this session): pin today's behavior with `// PIN-DEFECT(P0-2)` /
`// PIN-DEFECT(P0-6)` tests — the AI run is bound to the caller's cancellation token
(MapOwnerCommands TurnBudget/linked CTS), an aborted request kills the turn after the user
message persisted, and the MAF session persists only after stream completion
(`DirectAgentSession`). Then GREEN: replace and flip the pins.

## Target behavior
1. `chat.send` → durable queued turn (Pending) + `TurnId` in the response; queue head starts an
   Execution running the responder; turn transitions Pending → Running → Completed/Failed/
   Cancelled are durable and visible in the transcript/events.
2. The POST response MAY keep streaming deltas as a pure OBSERVER of the running turn — abort
   detaches, never cancels (existing SSE `/chats/{name}/events` stays the resume surface; keep
   event shapes additive — clients tolerate unknown `$type`).
3. `chat.cancel-turn` command (versioned, idempotent) cancels the active or queued turn;
   cancelling the head advances the queue.
4. Reconnecting clients see the truth: queued/running/completed/failed states are derivable
   from the events stream after a refresh (extend `ChatDelta`/transcript facts additively).
5. MAF session safe-point persistence + drift reset retained (P0-6 pin flipped).
6. A silo restart mid-turn leaves a durably Pending/Running turn that completes or fails
   durably after restart — never a vanished turn (spike-style test via the S1.4 fixtures).

## Design discipline
Study first: `MapOwnerCommands` (TurnBudget/CTS), `Chat.cs` (OwnerCommand log, SendStreaming,
responder resolution, FlushOutbox arm-before-offer), `DirectAgentSession` persistence,
S1.4's `IExecution` surface + test workers, and the S1.2/S1.3 actor stamping. Respect all 9
kernel traps (esp. 1: use FlushOutboxAsync where a turn must make its own sends visible; 2:
confirm routes before emitting new fact types). TDD. No new packages. No git.

## Out of scope
Conversation module extraction, Gmail (S1.6), Flutter client changes (events stay compatible),
CI, docs.

## Definition of done
Gate green; P0-2/P0-6 pins flipped with markers removed; FIFO proven (two queued turns run in
order; second conversation runs concurrently); browser-abort-detaches proven; restart-mid-turn
proven; cancel command proven; actor-refusal at grain proven; report maps behavior → test.
