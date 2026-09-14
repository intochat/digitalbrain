# Coding programme status

Read this file and `NOTES.md` first after a context reset; resume from the current task.

| Field | Value |
|---|---|
| Current phase | 1 (edits as transactions), branch `feature/coding-phase1-changesets` from phase 0 |
| Current branch | `feature/coding-phase0-workspace` (from `master` at `1e4b395d`) |
| Plan | `docs/coding/plans/2026-09-14-coding-phase1-changesets.md` (phase 0: `plans/2026-09-13-coding-phase0-workspace.md`) |
| Current task | Phase 1 Task 5 (the changeset neuron) in progress; Tasks 1-4 complete |
| Last green gate | `a7d2a751` (2026-09-14): format + build 0 warnings + `dotnet test DigitalBrain.slnx` 357 passed, 5 skipped, 0 failed |
| Last commit | `a7d2a751` coding: change-set edits attribute errors to every file they touched |
| Open PRs | #91 phase 0 (https://github.com/intochat/digitalbrain/pull/91) |

## Phase ledger

| Phase | Branch | Status | PR |
|---|---|---|---|
| 0 workspace and map | `feature/coding-phase0-workspace` | complete, PR open | #91 |
| 1 edits as transactions | `feature/coding-phase1-changesets` | in progress (task 5 of 9) | — |
| 2 slots | — | not started | — |
| 3 swarm | — | not started | — |
| 4 learning | — | not started | — |
| 5 pretty map and depth | — | not started | — |

Execution record per task (rulings, review rounds) lives in `.superpowers/sdd/<plan>/progress.md` while a phase runs; deviations and live observations go to `NOTES.md`.
