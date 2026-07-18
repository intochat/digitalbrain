---
name: operate
description: Resume the DigitalBrain v6 simplification roadmap. Reads docs/final-simplification/PROGRESS.md to decide the next action, executes the next concrete step, then writes status + a session-log entry back. Use when the user says "operate", "/operate", "resume", "continue the v6 work", or "what's next."
---

# operate — v6 phase-aware operator

You are resuming the **DigitalBrain v6 simplification roadmap**. The
plan, the roadmap, the risk register, and the decisions are all in
`docs/final-simplification/`. The single source of truth for "what
state is the work in" is `docs/final-simplification/PROGRESS.md`.

This skill **overrides** the user-global `operate` skill, which was
scoped to the v3 roadmap. v3 is closed; v6 is the active phase.

## On entry

1. **Read `docs/final-simplification/PROGRESS.md` first.** Do not
   start work without it. The "Next concrete action" section names
   what to do; the step-status table tells you what's done.
2. **Read the relevant step doc.** If "Next concrete action" points
   at V6-1, read `02-WINDOWS-AUTOSTART.md`. If V6-2, read
   `06-CONSTRUCTOR-UI.md` §3. The roadmap doc 09 indexes step → spec.
3. **Read the broader context if needed.** `00-OVERVIEW.md` and
   `09-ROADMAP.md` give the big picture. `10-RISKS-AND-DECISIONS.md`
   carries the decision log so you don't re-litigate settled points.
4. **Check git state.** `git status` for in-flight changes that the
   previous session may have left uncommitted.

## During work

- Follow CLAUDE.md non-negotiables. Especially: no `flutter test`, run
  `dotnet test` from root, never bypass hooks, use `aspire start` /
  `aspire stop` (not direct `dotnet run --project`), use the `LSP`
  tool for symbol navigation.
- One step from PROGRESS.md per session unless the user explicitly
  asks for more. Steps are sized to be one session each (3-5 days of
  work for a human; a few hours for an agent doing focused
  implementation).
- For sub-step granularity (`tray icon → hotkey → URL scheme → HKCU`
  per V6-1), commit each as a separate commit on the same branch.
- When a doc says "approximately N LOC", treat it as a rough budget,
  not a target. The doc's design constraints are what matter.

## On exit

Before yielding control back to the user, update
`docs/final-simplification/PROGRESS.md` in three places:

1. **Step status table:** flip the row's status (e.g., `not started`
   → `in progress`, or `in progress` → `done`). Stamp dates.
2. **Session log:** prepend a one-bullet entry. Format:
   `- YYYY-MM-DD — <one-line summary>. <commit SHA if any>.`
3. **Next concrete action:** rewrite if the next action has changed.
   If you finished a step, the next action is the next step's first
   sub-task. If you finished a sub-task within a step, the next
   action is the next sub-task.

Optionally update:

- **Definition of done:** flip a checkbox if a step's acceptance was
  met.
- **Blockers:** add an entry if you hit one you can't resolve.
- **Decisions awaiting human input:** add an entry if you encountered
  an ambiguity not yet decided in `10-RISKS-AND-DECISIONS.md`.

## When to stop

- A step requires a decision not in `10-RISKS-AND-DECISIONS.md`. Stop
  and ask the user.
- `dotnet test` goes red and you can't fix it within the session.
  Stop, commit a WIP branch, log the blocker in PROGRESS.md.
- The user explicitly says "stop" or "pause."

Do not continue past the boundary of one step without user
confirmation. Roadmap pacing is intentional.

## Anti-patterns (do not do these)

- **Don't relitigate decisions in `10-RISKS-AND-DECISIONS.md` §1.**
  They're settled. If you disagree, raise it as a §3 open question
  and stop.
- **Don't expand scope.** V6-1 is tray daemon scaffolding only — not
  also a Flutter window manager refactor. Stay within the step.
- **Don't skip the L6 gate.** Every `.ino` change goes through
  `InoAuthoringLoop.AuthorAsync`'s scenario gate, even tests.
- **Don't add a feature flag for the v6 cuts.** v6 is the trunk. The
  `--v5-compat` flag in roadmap §7 exists for deletions, not for new
  surfaces.
- **Don't write `flutter test`.** Per CLAUDE.md. UI assertions go via
  E2E tests under `DigitalBrain.E2E.Tests`.

## Quick-reference: docs index

| Step | Spec |
|------|------|
| Big picture | `docs/final-simplification/00-OVERVIEW.md` |
| Daily flow | `docs/final-simplification/01-USER-FLOW.md` |
| V6-1 | `docs/final-simplification/02-WINDOWS-AUTOSTART.md` |
| V6-7, V6-8 | `docs/final-simplification/03-INOLANG-CUT.md` |
| V6-7 | `docs/final-simplification/04-DB-PREFIX-AND-PORTS.md` |
| V6-8 | `docs/final-simplification/05-SCENARIOS.md` |
| V6-2, V6-3, V6-4 | `docs/final-simplification/06-CONSTRUCTOR-UI.md` |
| V6-9 (bonus) | `docs/final-simplification/07-3D-DEBUGGER.md` |
| V6-5, V6-6 | `docs/final-simplification/08-SERVER-CUTS.md` |
| All steps + sequencing | `docs/final-simplification/09-ROADMAP.md` |
| Decisions + open questions | `docs/final-simplification/10-RISKS-AND-DECISIONS.md` |
| Live tracker | `docs/final-simplification/PROGRESS.md` |
