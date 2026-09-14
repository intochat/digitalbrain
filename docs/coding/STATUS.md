# Coding programme status

Read this file and `NOTES.md` first after a context reset; resume from the current task.

| Field | Value |
|---|---|
| Current phase | 1 (edits as transactions), branch `feature/coding-phase1-changesets` from phase 0 |
| Current branch | `feature/coding-phase1-changesets` (from `feature/coding-phase0-workspace`, which is from `master` at `1e4b395d`) |
| Plan | `docs/coding/plans/2026-09-14-coding-phase1-changesets.md` (phase 0: `plans/2026-09-13-coding-phase0-workspace.md`) |
| Current task | Phase 1 complete: tasks 1-9, live exit check met (run 3: commit 272f9499 on local branch coding/rename-timer-alarm-20260914, suite green through the chat), whole-branch review + fix wave landed, final gates green; PR #92 open (base: the phase 0 branch). Next: phase 2 spikes S1-S4 on `feature/coding-phase2-slots` from this branch |
| Last green gate | `d7b0db44` (2026-09-14): format + build 0 warnings + `dotnet test DigitalBrain.slnx` 417 passed, 6 skipped (4 Docker + 2 coding gated), 0 failed; gated self-tests 2/2 at `5e223563` |
| Last commit | `d7b0db44` coding: the writer gate outlives its last lease; the cache fact waits for the cache; bounded diagnostics fan-out |
| Open PRs | #91 phase 0 (https://github.com/intochat/digitalbrain/pull/91), #92 phase 1 (https://github.com/intochat/digitalbrain/pull/92, stacked on #91) |

## Phase ledger

| Phase | Branch | Status | PR |
|---|---|---|---|
| 0 workspace and map | `feature/coding-phase0-workspace` | complete, PR open | #91 |
| 1 edits as transactions | `feature/coding-phase1-changesets` | complete, PR open | #92 |
| 2 slots | — | not started | — |
| 3 swarm | — | not started | — |
| 4 learning | — | not started | — |
| 5 pretty map and depth | — | not started | — |

Execution record per task (rulings, review rounds) lives in `.superpowers/sdd/<plan>/progress.md` while a phase runs; deviations and live observations go to `NOTES.md`.
