# Execution ledger — 2026-09-20-testing-framework-refactoring.md

Baseline: ed661d6b preserves all original working-tree changes plus research/plan.
Implementation workspace: C:/Users/vhorb/.codex/worktrees/testing-refactoring/digitalbrain.
Task 0: complete. Original failures reproduced in research: no endpoint (None), transient placeholder race (Web).
Pre-flight: Tasks 1/3 share E2E startup; introduce E2EOptions with Task 1 to avoid a temporary public signature.
Pre-flight: Tasks 2/3 share production composition; resolve definitions before projecting hosting settings.
Pre-flight: Tasks 1/4 share browser lifecycle; metadata-based readiness follows the initial regression fix.
Pre-flight: Tasks 5/6 share runner packaging; maintain process execution and private credential configuration.
Ruling: use a tracked execution ledger instead of bash helper scripts on Windows; persistent reviewable progress is equivalent.
Ruling: original checkout remains untouched; checkpoint its dirty changes only in isolated worktree.
Context7 resolution exhausted its monthly quota; use previously verified official docs and installed API metadata.

Tasks 1–3: implemented. Browser preferences tests RED (missing resolver) -> GREEN, 13 framework tests.
Actual UI regression: two-way test GREEN; subsequent both UiKit tests GREEN (2/2).
Dependency registration test RED ("Dependency was not configured before dependent") -> GREEN; framework suite 14/14.
Application transport tests RED (missing transport) -> GREEN, framework suite 22/22.
Ruling: introduce minimal E2EOptions/application contract with Task 1; avoid a transient public overload.
Ruling: move ModuleHostingAttribute into production core so a module can name an optional hosting adapter without referencing Aspire.
Tasks 4–5: implemented. Signal budget regression RED (outer cancellation instead of configured timeout) -> GREEN; Flutter unit suite 27/27.
Browser lifecycle/readiness tests pass; combined framework suite 25/25.
Google typed endpoint/private configuration integration tests RED (missing options) -> GREEN, 2/2.
External provider adapter, runner loading, restart and isolation suite GREEN, 6/6.
First solution run identified caller migrations: missing Inbox.Signals using, HTTP-only Expander accidentally requested Web, cancellation test assumed old hardcoded cleanup budget. Migrate each to current explicit contracts.
Ruling: keep production module bootstrap's private-config support narrow to the existing test configuration section; file is ACL restricted, owned by session, removed last.
Task 6 gate: programmatic DistributedApplicationTestingBuilder.Create failed with "No application host assembly was found ... Aspire.Hosting.AppHost ... Aspire.AppHost.Sdk".
NuGet pack also failed NU5039: root README.md missing from package. Do not claim external package portability.
Ruling: retain ModuleAppHost/ModuleRunner/Hosting under Infrastructure, exercising approved fallback. Remove throwaway programmatic probe. Linux/native package consumer verification is not claimed.
Ruling: Tasks 1–5 form one buildable migration checkpoint because public contract changes touch all callers; avoid committing deliberately uncompilable intermediate states.

Independent review found and fixed: eager Flutter defaults breaking explicit AddModule callbacks, unbounded unit startup rollback, cancellation gaps acquiring Playwright/context.
Explicit callback regression RED (automatic Window launched before callback) -> GREEN.
Unit startup now owns cluster before deployment and attaches rollback failures to the original error.
Browser acquisition closes late protocol results after cancellation; regression test passes.
Framework suite after review: 27/27. Runtime suite after explicit cleanup-budget migration: 23/23.
First broad run: 193/195 passed; the two failures were the now-migrated Expander HTTP hosting choice and hardcoded cleanup-budget expectation. All 6 application scenarios passed.

Second broad run: 198/199 passed. SubscriptionFailureFacts observed a failure notification before the renewal loop's finally block had completed cleanup. Await explicit subscription disposal before asserting membership removal; failure notification intentionally remains prompt. This removes a race in the assertion without changing runtime semantics.

Final broad production-code gate: 199/199 passed, zero skipped, 2m 11s (`final-verified-solution.log`).
Additional acceptance coverage then passed: framework 32/32 and Flutter unit 28/28. These add explicit headed defaults, invalid browser budgets, different simultaneous run budgets, conflicting settings across modules, cancellation during navigation and diagnostic message redaction. Only test/documentation files changed after the broad gate.
Solution organization committed independently as 7ee2a929. No ModuleAppHost deletion was made.

Real two-way Flutter UI matrix: three headless/zero-delay runs and three headed/250 ms runs all passed independently (six selected tests, no retries). Logs: ui-headless-{1,2,3}.log and ui-headed-{1,2,3}.log. Browser mode came from the documented per-process environment preference; resolver tests independently prove explicit options override that preference.
Final scope: four public libraries, retained internal infrastructure, migrated callers and documented API. External NuGet consumer, Linux/native asset certification and ModuleAppHost removal remain deferred by the approved failed-gate fallback. Pure-Dart/native desktop launches were not part of the executed browser matrix.
