# Stage 2 implementation and verification

Implemented on `codex/programmable-behaviors`. The user requested committing the completed work together with its stage-1 AI foundation. No push or merge was performed.

## Delivered

* Versioned `ICodeDraft` source/tests, durable command receipts, pinned checks, bounded diagnostics and cancellation.
* Real isolated compilation and xUnit/MTP execution; only passing checked payloads become content-addressed artifacts. Activation verifies complete payload and current environment without rebuilding.
* `IBehaviorProgram` deployment/start/stop/rollback, retained artifact/configuration revisions, generation readiness, live notifications and bounded durable logs.
* Windows job ownership before execution, authenticated control pipe, heartbeat/stop deadlines, child-tree cleanup, confirmed-exit replacement, bounded retry and durable desired-state recovery.
* `BehaviorApp.RunAsync<T>` using the existing `IBehavior`, scoped `IDigitalBrain`, installed Orleans contracts and required subscription tracking. The standalone timer example builds using this bootstrap.
* IntoChat authoring through `IAgent`, three-candidate repair, overall deadline, cancellation, host activation policy, workspace-scoped native/MCP tools and HTTP endpoints.

## Verification run

| Command/project | Result |
| --- | --- |
| `dotnet build DigitalBrain.slnx` | Passed; 0 warnings, 0 errors |
| Coding unit project | 49 passed |
| Behavior unit project | 11 passed |
| Existing Behaviors unit project | 5 passed |
| AI unit project | 42 passed |
| IntoChat unit project | 17 passed |
| DigitalBrain Runtime unit project | 47 passed |
| AI E2E project | 1 passed |
| Behavior E2E project | 1 passed |
| `dotnet build src/Behaviors/timer-report.cs -p:CodeGraphRefresh=false` | Passed |
| `git diff --check` | Passed |

Total: **173 passing tests** across eight projects. Logs are retained locally in `.superpowers/sdd/2026-09-22-programmable-behaviors/`.

The Behavior E2E scenario uses scripted model HTTP responses, actual IntoChat authoring, real compilation/tests, a separate Aspire silo and a separate behavior worker. It verifies timer-to-Flutter output, artifact identity and distinct worker PID, source update, stop, rollback, normal completion without restart, syntax rejection, missing subscription readiness, a crashing behavior, forced host termination, child exit, recovery with a new generation, and preservation of stopped intent. No live model credentials are used.

Unit coverage includes concurrent revision writes, enumeration during atomic replacement, pinned input after edits, interrupted-check recovery, artifact tampering and environment changes, cross-instance quota locking/deduplication, failing/hanging tests, bounded output and descendant cleanup, durable receipts, stop during launch, failed-stop replacement prevention, retry exhaustion, log truncation, authoring repair/refusal/cancellation and shared native/MCP scope/results. Existing runtime tests cover live-subscription overflow and source reactivation.

## Review and implementation adjustments

Independent review findings were fixed: control EOF racing normal process exit; Windows state enumeration interfering with replacement; per-instance artifact quota locks; cached activation fingerprints; receipt lookup after artifact verification; and swallowed exit-confirmation failure. Regression tests cover these coordination and persistence paths.

The restart test exposed an existing Aspire test-harness dependency on the synchronous caller stack. Builder creation now precedes asynchronous private-configuration writes; the fixture keeps its metadata-bearing start frame. The restarted host and AI E2E both pass with that change.

Host-only process/artifact helpers are reused from Coding's implementation assembly rather than duplicated through linked shared source. E2E source/test fixtures are embedded in the scenario. Lifecycle tests use bounded real-time deadlines rather than a fake clock. The original plan remains a design record; this document records the actual implementation and checks rather than marking unexecuted historical red/green or commit steps complete.

## Operating scope

This is a trusted local **Windows, single-host** executor. It is not an untrusted-code sandbox, a shared-disk multi-silo scheduler or a durable signal replay system. The tests do not imply exactly-once effects. Live model behavior and other OS backends are not covered. See `src/Modules/Behavior/README.md` for configuration, limits, recovery and retention rules.
