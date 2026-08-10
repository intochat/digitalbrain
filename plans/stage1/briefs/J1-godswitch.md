# J1 — delete the last keyword god-switch (W2 cleanup)   (role: RED+GREEN combined)

Report path: `plans/stage1/reports/J1-godswitch.md`

## Objective
Remove the last hardcoded keyword demo from the Chat neuron and complete the ratified W2 cleanup:

1. **Delete** the `WantsTimeButton`/`ShowTime` keyword demo path in the UI module's Chat neuron
   (the code that scans chat text for words like "time"/"button" and offers a time button).
   Keyword god-switches are banned (kernel trap 9). Delete the demo's dedicated tests with it.
2. **Delete** any now-dead contracts/transforms that existed ONLY for that demo (e.g. a
   `ShowTime` synapse / `ButtonActivatedToShowTime` transform) — but ONLY if nothing else
   references them. Search the whole solution first, including the graph/vocabulary wiring and
   DI hooks (`Core.IModule` implementations). If a UI-module DI hook registers only that demo
   transform, remove the registration; keep the hook class if other registrations remain.
3. **Add `Author` to the `Responded` synapse** (the ratified W2 item): the author identity of a
   reply. Keep the wire alias unchanged (field addition is additive). Thread the value from the
   responder path; update every construction site. Choose the minimal honest representation
   (e.g. a string author/agent name) — do NOT invent an identity system here; the identity seam
   comes later.
4. Do NOT rename `Message`/`Reply` (that optional W2 item is deferred by the orchestrator).

## Method (TDD, per GROK.md)
- FIRST write/extend characterization tests that pin the chat behavior that must SURVIVE:
  plain send → responder reply appended with Author populated; `ui.note` → transcript line;
  `ui.timer-card` → clock offer. Run them green against current code where possible.
- Then delete the demo path; adjust tests; run the full gate.
- Timer module (`time.*`) functionality must remain fully intact — the timer feature is real;
  only the keyword-triggered demo shortcut dies.

## In scope
`src/Modules/UI/**` (Chat neuron + its contracts + module DI hook), the demo's tests in
`src/Tests/DigitalBrain.Tests`, and ONLY the `Responded` contract + its construction sites.

## Out of scope
Everything else. No renames, no auth, no OAuth, no Execution/Tasks, no Flutter, no new packages,
no git.

## Definition of done
`pwsh scripts/gate.ps1` passes (0 warnings, all tests green); zero references to the deleted
demo remain (`WantsTimeButton`, `ShowTime`, demo transform) anywhere in `src/`; report written.
