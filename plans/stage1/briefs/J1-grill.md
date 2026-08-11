# J1-GRILL — adversarial review of the god-switch removal   (role: GRILL)

Report path: `plans/stage1/reports/J1-grill.md`

You are a hostile reviewer with NO stake in the change. The working tree contains an uncommitted
change made by another session executing `plans/stage1/briefs/J1-godswitch.md`. Your job is to
try to REFUTE it. Default to skepticism.

## Procedure
1. Read `GROK.md`, the brief `plans/stage1/briefs/J1-godswitch.md`, and the worker's report
   `plans/stage1/reports/J1-godswitch.md`.
2. Inspect the actual change: `git status --porcelain`, `git diff` (read ALL of it).
3. Attack, in order:
   - **Scope**: any file touched outside the brief's scope? Any rename/refactor smuggled in?
   - **Kernel traps**: zero-receiver emissions (trap 2 — did a deleted route leave an emitter
     firing into nothing?); broadcast catalog changes (trap 8 — did deleting/adding an
     `IHandle<T>` change ghost spawning?); settled-vs-retried (trap 4).
   - **Survivors**: timer feature (`time.*` → chat clock offer) must still work — find the test
     that proves it. `ui.note`, `ui.timer-card`, responder resolution must still work.
   - **Completeness**: run `git grep -n "WantsTimeButton\|ShowTime"` — anything left? Any
     orphaned contract/transform/test/registration?
   - **Responded.Author**: is it populated on EVERY construction path, or does some path emit
     empty/default? Is the wire alias untouched?
   - **Quality**: dead usings, leftover comments, commented-out code, TODO litter?
4. Verify the gate yourself: `dotnet build DigitalBrain.slnx` then
   `& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe`.

## Verdict format (end of your report)
`VERDICT: APPROVE` or `VERDICT: REJECT` followed by a numbered list of findings, each with
file:line and severity (BLOCKER / MAJOR / MINOR). REJECT if any BLOCKER exists. Do NOT fix
anything yourself — you only judge.
