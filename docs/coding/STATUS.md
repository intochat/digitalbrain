# Coding programme status

Read this file and `NOTES.md` first after a context reset; resume from the current task.

| Field | Value |
|---|---|
| Current phase | 1 (edits as transactions), branch `feature/coding-phase1-changesets` from phase 0 |
| Current branch | `feature/coding-phase0-workspace` (from `master` at `1e4b395d`) |
| Plan | `docs/coding/plans/2026-09-14-coding-phase1-changesets.md` (phase 0: `plans/2026-09-13-coding-phase0-workspace.md`) |
| Current task | Phase 1 Task 1 (contracts) under review; plan `docs/coding/plans/2026-09-14-coding-phase1-changesets.md` (9 tasks) |
| Last green gate | `8f397b42` (2026-09-14): format + build 0 warnings + `dotnet test DigitalBrain.slnx` 333 total, 328 passed, 5 skipped, 0 failed; gated self-test passed on the real solution; Flutter: dart format 0 changed, analyze clean, ui 22/22, shell 49/49 |
| Last commit | `8f397b42` coding: apply the phase 0 branch review (reload after warmup, query gate, envelope, dead surface) |
| Open PRs | #91 phase 0 (https://github.com/intochat/digitalbrain/pull/91) |

## Phase ledger

| Phase | Branch | Status | PR |
|---|---|---|---|
| 0 workspace and map | `feature/coding-phase0-workspace` | complete, PR open | #91 |
| 1 edits as transactions | `feature/coding-phase1-changesets` | in progress (task 1 of 9) | — |
| 2 slots | — | not started | — |
| 3 swarm | — | not started | — |
| 4 learning | — | not started | — |
| 5 pretty map and depth | — | not started | — |

Execution record per task (rulings, review rounds) lives in `.superpowers/sdd/<plan>/progress.md` while a phase runs; deviations and live observations go to `NOTES.md`.
