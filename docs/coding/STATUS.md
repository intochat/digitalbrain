# Coding programme status

Read this file and `NOTES.md` first after a context reset; resume from the current task.

| Field | Value |
|---|---|
| Current phase | 0 (workspace and map) |
| Current branch | `feature/coding-phase0-workspace` (from `master` at `1e4b395d`) |
| Plan | `docs/coding/plans/2026-09-13-coding-phase0-workspace.md` |
| Current task | Task 5: the `workspace` neuron (in progress); Tasks 1-4 complete |
| Last green gate | Task 4 commit `20321ae8`: format + build (0 warnings) + `dotnet test DigitalBrain.slnx` 313 passed, 5 skips (4 Docker + coding self-test); gated self-test green on the real solution, 38 projects, 0 load failures, ~18 s (2026-09-13) |
| Last commit | `20321ae8` coding: a malformed solution path is a warmup warning, not a silo failure |
| Open PRs | none |

## Phase ledger

| Phase | Branch | Status | PR |
|---|---|---|---|
| 0 workspace and map | `feature/coding-phase0-workspace` | in progress (task 5 of 9) | — |
| 1 edits as transactions | — | not started | — |
| 2 slots | — | not started | — |
| 3 swarm | — | not started | — |
| 4 learning | — | not started | — |
| 5 pretty map and depth | — | not started | — |

Execution record per task (rulings, review rounds) lives in `.superpowers/sdd/<plan>/progress.md` while a phase runs; deviations and live observations go to `NOTES.md`.
