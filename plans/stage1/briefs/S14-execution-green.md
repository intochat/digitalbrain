# S1.4-GREEN — Execution kernel: rename Tasks → Execution and deepen it   (role: GREEN)

Report path: `plans/stage1/reports/S14-execution-green.md`

## Ratified constraints (binding — RATIFIED-PRODUCT-DEFINITION.md §1.10, §4.3/4.4)
- Rename `DigitalBrain.Modules.Tasks` → `DigitalBrain.Modules.Execution` with `IExecution` +
  `ExecutionNeuron`. **No compatibility layer** — the module has no production callers (verify,
  then state it in the report).
- Vocabulary (already in CONTEXT.md): Task / **Execution** (one durable run) / **Attempt**
  (retry generation) / **Operation** (one externally observable effect) / **Blocker** (why an
  Execution waits: OAuth, user input, timer, dependency, uncertain outcome).
- Hybrid interface: minimal deep external surface `Apply`/`Read` (+ versioned `Cancel` as an
  Apply command); attempts, workers, reminders, cursors, operation phases, blocker custody stay
  INTERNAL.
- After a non-idempotent external operation starts, an unknown outcome NEVER auto-retries →
  `OutcomeUncertain` until explicitly reconciled.
- Orleans-owned durability. No MAF types anywhere in this module. Not a general workflow engine
  — no wait sets, no child executions, no generic signaling.

## Objective
1. **Rename** project, namespaces, grain types, aliases (module vocabulary becomes
   `db.execution.*`; the old module's wire aliases may be renamed because the module has no
   production callers and no real data — confirm by search and record in the report).
2. **Fix P0-8 defects** (RED pins exist only where earlier sessions pinned Tasks behavior —
   check `git grep PIN-DEFECT`; characterize-as-you-go where coverage is missing):
   - Command receipts + operation ledger get bounded retention (follow the pattern used by
     chat transcript retention).
   - `OutcomeUncertain` gets an explicit reconciliation path: an Apply command
     (`ResolveOperation` — completed | failed | permit-retry) that an operator/behavior can
     fire; until resolved, the execution stays blocked (Blocker kind: uncertain outcome).
   - Operation identity becomes attempt-stable: the same logical operation across retries
     carries the same operation key, so a completed effect is never repeated by a retry.
     Sequence-only identity dies.
   - Delete or implement the unimplemented supervised worker methods (`GroupChat.cs:50`-style
     dead throws) — decide by whether the Conversation adapter (S1.5) needs them; if deleted,
     say so.
3. **Spike harness** (ratified Stage-1 exit test, built now as tests): one execution driven
   through — silo restart (cluster restart in-test), blocker wait + resume (simulated OAuth),
   explicit cancel, duplicate submission (same command id → one execution), and an uncertain
   external write that stays `OutcomeUncertain` without auto-retry, then resolves via the
   reconciliation command. Use the in-process cluster fixtures; simulate externals with test
   workers.

## Design discipline
Study the existing Tasks module thoroughly FIRST (TaskNeuron.Commands/Dispatch/Operations,
AttemptFacts, worker dispatch relay, reminders). Salvage the sound mechanics (idempotent
command receipts, optimistic revisions, durable redispatch, effect ledger phases); replace the
unsound ones. Keep the module = contracts + neurons. Settled refusals via
`NeuronAuthorizationException`. TDD. No new packages. No git.

## Out of scope
Conversation/chat wiring (S1.5 is the first production adapter), MCP calls, HTTP, Flutter.

## Definition of done
Gate green; spike harness tests green; zero references to the old Tasks module names remain in
src/ (docs excluded); report includes: what was salvaged vs replaced and why, the external
surface (exact contract shapes), and the spike matrix results table.
