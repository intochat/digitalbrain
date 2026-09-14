# Coding programme status

Read this file and `NOTES.md` first after a context reset; resume from the current task.

| Field | Value |
|---|---|
| Current phase | 1 (edits as transactions), branch `feature/coding-phase1-changesets` from phase 0 |
| Current branch | `feature/coding-phase1-changesets` (from `feature/coding-phase0-workspace`, which is from `master` at `1e4b395d`) |
| Plan | `docs/coding/plans/2026-09-14-coding-phase1-changesets.md` (phase 0: `plans/2026-09-13-coding-phase0-workspace.md`) |
| Current task | Phase 1 gate: tasks 1-9 complete; live exit check, whole-branch review, final gates and PR pending |
| Last green gate | `e175c188` (2026-09-14): format + build 0 warnings + `dotnet test DigitalBrain.slnx` 391 passed, 6 skipped (4 Docker + 2 coding gated), 0 failed |
| Last commit | `e175c188` coding: phase 1 docs match the code; the chat fact names its tool sequence |
| Open PRs | #91 phase 0 (https://github.com/intochat/digitalbrain/pull/91) |

## Phase ledger

| Phase | Branch | Status | PR |
|---|---|---|---|
| 0 workspace and map | `feature/coding-phase0-workspace` | complete, PR open | #91 |
| 1 edits as transactions | `feature/coding-phase1-changesets` | tasks complete, phase gate in progress | — |
| 2 slots | — | not started | — |
| 3 swarm | — | not started | — |
| 4 learning | — | not started | — |
| 5 pretty map and depth | — | not started | — |

Execution record per task (rulings, review rounds) lives in `.superpowers/sdd/<plan>/progress.md` while a phase runs; deviations and live observations go to `NOTES.md`.
