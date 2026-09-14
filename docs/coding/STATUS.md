# Coding programme status

Read this file and `NOTES.md` first after a context reset; resume from the current task.

| Field | Value |
|---|---|
| Current phase | 2 (slots), branch `feature/coding-phase2-slots` from phase 1 |
| Current branch | `feature/coding-phase2-slots` (from `feature/coding-phase1-changesets` at `9fd09c46`; phase 1 from phase 0, phase 0 from `master` at `1e4b395d`) |
| Plan | `docs/coding/plans/2026-09-14-coding-phase2-slots.md` (11 tasks; phase 1: `plans/2026-09-14-coding-phase1-changesets.md`, phase 0: `plans/2026-09-13-coding-phase0-workspace.md`) |
| Current task | Phase 2: spikes S1-S4 done (NOTES "Phase 2 spikes", design 4.4 amended twice: standby as an executable over its artifacts, lease in a dedicated table); Task 9 (`code_promote`) in progress; Tasks 1-8 complete |
| Last green gate | `820e9eab` (2026-09-14): format + build 0 warnings + `dotnet test DigitalBrain.slnx` 508 passed, 13 skipped (4 Docker + 3 coding gated + 6 lease gated), 0 failed; gated slot build 1/1 into artifacts/slot-b at `db0e0c81` |
| Last commit | `820e9eab` coding: the gateway references the brain as a client and the shell names it as its front door |
| Open PRs | #91 phase 0 (https://github.com/intochat/digitalbrain/pull/91), #92 phase 1 (https://github.com/intochat/digitalbrain/pull/92, stacked on #91) |

## Phase ledger

| Phase | Branch | Status | PR |
|---|---|---|---|
| 0 workspace and map | `feature/coding-phase0-workspace` | complete, PR open | #91 |
| 1 edits as transactions | `feature/coding-phase1-changesets` | complete, PR open | #92 |
| 2 slots | `feature/coding-phase2-slots` | in progress (task 9 of 11) | — |
| 3 swarm | — | not started | — |
| 4 learning | — | not started | — |
| 5 pretty map and depth | — | not started | — |

Execution record per task (rulings, review rounds) lives in `.superpowers/sdd/<plan>/progress.md` while a phase runs; deviations and live observations go to `NOTES.md`.
