# Coding programme status

Read this file and `NOTES.md` first after a context reset; resume from the current task.

| Field | Value |
|---|---|
| Current phase | 2 (slots), branch `feature/coding-phase2-slots` from phase 1 |
| Current branch | `feature/coding-phase2-slots` (from `feature/coding-phase1-changesets` at `9fd09c46`; phase 1 from phase 0, phase 0 from `master` at `1e4b395d`) |
| Plan | `docs/coding/plans/2026-09-14-coding-phase2-slots.md` (11 tasks; phase 1: `plans/2026-09-14-coding-phase1-changesets.md`, phase 0: `plans/2026-09-13-coding-phase0-workspace.md`) |
| Current task | Phase 2: spikes S1-S4 done (NOTES "Phase 2 spikes", design 4.4 amended twice: standby as an executable over its artifacts, lease in a dedicated table); plan written, reviewed and fixed (5609 lines, 11 tasks); pre-flight scan, then Task 1 |
| Last green gate | `d7b0db44` (2026-09-14): format + build 0 warnings + `dotnet test DigitalBrain.slnx` 417 passed, 6 skipped (4 Docker + 2 coding gated), 0 failed; gated self-tests 2/2 at `5e223563` |
| Last commit | see git log (phase 2 plan commit) |
| Open PRs | #91 phase 0 (https://github.com/intochat/digitalbrain/pull/91), #92 phase 1 (https://github.com/intochat/digitalbrain/pull/92, stacked on #91) |

## Phase ledger

| Phase | Branch | Status | PR |
|---|---|---|---|
| 0 workspace and map | `feature/coding-phase0-workspace` | complete, PR open | #91 |
| 1 edits as transactions | `feature/coding-phase1-changesets` | complete, PR open | #92 |
| 2 slots | `feature/coding-phase2-slots` | plan committed, implementation starting | — |
| 3 swarm | — | not started | — |
| 4 learning | — | not started | — |
| 5 pretty map and depth | — | not started | — |

Execution record per task (rulings, review rounds) lives in `.superpowers/sdd/<plan>/progress.md` while a phase runs; deviations and live observations go to `NOTES.md`.
