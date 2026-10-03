# App scenarios, identity and marketplace implementation plan

> Execute with the subagent-driven-development workflow. The user approved the design and explicitly requested documentation followed by implementation and a PR on a new branch.

## Goal and agreed design

Make apps easy to compose and edit as small scenario blocks, publish them under `user/appid`, and make app-owned Postgres data discoverable through an authorized path. Reuse existing identity and package abstractions instead of introducing a second ownership system.

The principal complexity is the mismatch between behavior files, installed app identity, prose scenarios, and tests. `ScriptEdge` currently stamps a file ID as `CallerContext.AppId`; Postgres uses that for ownership. The assistant SQL provider independently uses the platform database even when table neurons use provisioned capacity. The reported generic assistant error is not yet reproduced; it must not be described as conclusively diagnosed.

## Global constraints

- Preserve `PackageId(owner, name)`, `CallerContext`, account-aware `BrainScope`, `IIdentity`, and `CapacityScope`.
- Installed `IApp` grain keys are stable app identities. File names, generations and scenario IDs are not ownership identities.
- Only trusted host code may bind an execution file to an app; script settings and inherited caller AppId cannot authorize that binding.
- Standalone scripts retain file-scoped identity. Cross-brain and cross-app access remain denied.
- Preserve existing saved tables and pinned origins through retry-safe migration. Uninstall retires app storage and legacy scopes.
- Keep C# contracts and signals as executable behavior. Do not add a runtime natural-language interpreter or general graph engine.
- Keep existing Specs Gherkin support compatible; new app authoring uses scenario blocks over ordinary C# behaviors and deterministic tests.
- Append serializer IDs; mirror changed wire shapes in Dart. No live models in deterministic verification.
- Build and test by project, never the solution (repository guidance).

## Affected areas and tasks

### 1. Trusted app identity and storage lifetime

Affected: `Modules/Microsoft/CSharp` execution contracts, file state/start/authorization, edge stamping and unit/E2E tests; `Modules/DigitalBrain/Apps` deployment, upgrade, restart, uninstall, storage history and tests; `Modules/Postgres` table ownership, owner indexes, lifecycle guard, migration and tests. SDK/Platform identity types are reused, not replaced.

- [x] Test that two behavior files in one installed app share identity, standalone files retain identity, and untrusted callers cannot bind arbitrary app IDs.
- [x] Add a host-only app/file execution binding validated against the originating Apps grain; persist and return it with run authorization. Stamp verified identity at the script edge.
- [x] Deploy behaviors with the installed app identity. Carry migration history from all recorded legacy file scopes.
- [x] Migrate table ownership without moving/renaming physical tables or changing pinned capacity origin; preserve retry safety and reject unrelated owners.
- [x] Migrate file-scoped permission grants through the existing brain identity authority, preserving modes, conversations, timestamps and revocation state. Atomic per-file migration receipts prevent retries from restoring revoked or consumed permissions.
- [x] Retire both stable app storage and historical file storage on uninstall; verify configuration/upgrade does not lose rows.
- [x] Run CSharp, Apps, and Postgres unit suites plus relevant E2E tests.

### 2. Authorized data discovery and assistant diagnostics

Affected: `Modules/Postgres` tool factory, source/provider selection and table resource discovery; `Modules/DigitalBrain/Assistant` instructions/tool policy/error reporting and tests; Apps installation listings as authorization evidence; Registry only if actual resource discovery requires it.

- [x] Reproduce routing mismatch with a test where table capacity is not the platform database.
- [x] Expose discoverable app table resources with installation identity and pinned origin through a host-authorized path; preserve membership and ownership checks.
- [x] Let assistant table tools select a discovered resource explicitly and route schema/read windows through its real origin. Preserve existing platform Postgres behavior.
- [x] Retain actionable errors and correlation in server logs; do not expose secrets or replace failures with successful-looking results.
- [x] Verify unavailable sources, cross-brain denial, legacy platform tables and provisioned tables.

### 3. Scenario-first UI and marketplace

Affected: Flutter shell `workspace/apps` screens, authoring document models/editors, scenario/verification display, marketplace cards and widget tests; Apps authoring contracts/codec/prompts only if needed for stable scenario bindings.

- [x] Make each scenario a visible standalone block with trigger/outcome prose, linked source and test status. Preserve stable IDs while editing/duplicating/reordering.
- [x] Keep behavior source mappings compatible but show scenarios as the primary authoring unit, including shipped packages without metadata.
- [x] Present marketplace identity as `owner/name`; clearly distinguish published catalog entries and installed apps, with search and existing install/fork/spec actions.
- [x] Add widget coverage for scenario cards, source links, legacy documents, verification status and namespaced marketplace entries.
- [x] Run Flutter analyze and shell tests.

### 4. Thin researcher scenarios and test support

Affected: `IntoChat/Apps/CustomerResearcher` manifest/spec/behaviors/tests; Apps client/testing support and authoring prompts; shipped-content and researcher E2E tests. Specs module receives only changes actually needed for compatibility/documentation, rather than a duplicate execution framework.

- [x] Share invocation/subscription plumbing and scratch-install test lifecycle in small reusable helpers available to scripts.
- [x] Keep scenario handlers thin: open UI, submit research, run research, stop, inspect results. UI buttons and app operations converge on the same invocation path.
- [x] Keep the bounded browser/model research algorithm ordinary code; do not fragment its internal method calls into workflow steps.
- [x] Use stable scenario IDs for verification where metadata exists, retaining name-based compatibility for old packages.
- [x] Update authoring guidance to produce small trigger/outcome scenarios and use reusable test plumbing.
- [x] Verify deterministic researcher behavior, refusal, cancellation, saving and reading data; compile live scenarios when external services are unavailable.

### 5. Integration, review and PR

- [x] Run affected C# unit and E2E projects, Flutter analyze/tests, IntoChat composition checks, and attempt Aspire smoke.
- [x] Review all changes for privilege escalation, storage loss, retry/restart behavior, stale verification, and mismatched Dart/C# contracts.
- [x] Record actual results and limitations below, commit on `codex/app-scenarios-identity`, push and create a PR. Do not merge.

## Review focus

1. A script must not impersonate another app via settings or caller inheritance.
2. Upgrade/reconfigure and interrupted migrations must preserve existing rows and origin.
3. An assistant must not discover/query another brain's app data through platform SQL privileges.
4. Renaming/reordering scenarios must not attach another scenario's test result.
5. Legacy shipped packages without authoring metadata must remain readable, runnable and verifiable.

## Execution record

- Pull request: https://github.com/intochat/digitalbrain/pull/131 (base `master`; not merged).
- Initial checkout clean on `master`; created `codex/app-scenarios-identity` in the existing checkout as requested.
- Ruling: user explicitly requested document then implementation and PR; proceed without another design-approval round.
- Ruling: use existing C# behaviors and signals for scenario execution; earlier fluent API examples were sketches, not a mandate to introduce a competing workflow language.
- Shared interfaces: identity task owns Postgres ownership/lifetime files; data task owns Postgres tools/providers/discovery files. Coordinate any new shared contracts before editing.
- Shared interfaces: UI keeps existing authoring document contracts unless coordinated; researcher work supplies structured metadata matching those contracts.

## Implemented interfaces and affected project boundaries

| Area | Concrete change |
| --- | --- |
| CSharp contracts/runtime | Host-only `ICSharpAppBinding`; persisted app/brain binding; current-run token authorization; bound-file mutation guard. |
| Apps Workspace | `App.Deploy` binds files and migrates recorded old storage owners; uninstall retires stable and legacy scopes. |
| Postgres contracts/runtime | Host-only `IPostgresAppStorage`; retry-safe ownership transfer retains physical table and pinned origin, including pending DDL. |
| Postgres discovery/tools | `PostgresAppResources`, table descriptors, fixed-projection `PostgresAppLiveSource`; `postgres_schema(appTables: true)` and `show_postgres_query_table(resource: ...)`. Partial discovery reports individual failures without hiding healthy tables. |
| Supabase shared window infrastructure | `LiveTableWindows` recognizes the Postgres app-resource source prefix in source policy checks; normal Supabase SQL behavior is unchanged. |
| Assistant | App-table guidance, server scope/thread/run correlation and a safe run ID in the generic failure response. |
| Apps contracts/verification | `AppScenarioSuite`, `AppCalls`, `AppInvocations`; optional appended `AppScenarioVerdict.ScenarioId`; JSON result protocol and complete structured scenario coverage. Legacy specs retain name matching. |
| Apps authoring | Builder uses shared thin-test helpers; scenarios describe triggers/outcomes; harmless terminal spec newlines do not break metadata matching. |
| Flutter shell | Scenario-first cards with source/status/actions, stable-ID verification, unique duplicate names, namespaced catalog search and installed/published sections. |
| Customer Researcher | Structured authoring metadata; separate controls behavior forwards app operations; shared invocation recovery and thin test lifecycle. Browser/model algorithm stays ordinary C#. |
| Specs | Existing Gherkin engine retained for compatibility; 12 tests pass. App authoring does not introduce another interpreted scenario engine. |
| Identity/SDK/Platform | Host-only `IAppGrantMigration` on the existing scoped authority preserves grants with atomic per-file migration receipts. Existing `CallerContext`, `BrainScope`, `IIdentity`, `PackageId` and `CapacityScope` retained; no replacement identity subsystem. |

## Migration and operation

Existing installations acquire the stable app identity on their next **configure or upgrade**. This stops the old behavior generation, transfers indexed storage ownership and file-scoped permission grants, and starts bound behavior files. Merely deploying the server or reading an app does not mutate existing installations. The migration keeps table names and database origins unchanged; no row copy or destructive schema migration runs.

For an existing installation, reapply configuration with its current settings (an empty settings override preserves current declared values), or upgrade to the new published revision. A subsequent `postgres_schema(appTables: true)` discovers its app tables. Select the returned resource handle with `show_postgres_query_table`; do not construct physical table names or database origins in assistant prompts.

An app is still published as `owner/name`. Its installed `IApp` key is its runtime identity. A scenario ID and a source filename never become the resource owner.

## Review and validation record

- UI review: fixed duplicate scenario names by choosing the first unused suffix.
- Runtime/test review: fixed legacy ID display and skipped completed/pruned invocation signals using `Pending()`, which works across both Orleans and HTTP clients.
- Security/lifecycle review: trusted binding, same-brain mutation denial, stale bound tokens, migration and resource authorization reviewed; fixed one pending table suppressing all discovery. Scoped re-reviews approved.
- Final requirements audit added permission-grant migration to the existing scoped identity authority. Atomic receipts preserve revocation and consumed Once grants across retries, including a persisted migration whose response is lost. Independent security review found no remaining issues.
- Assistant E2E initially found an authentication fixture expecting 401 under the explicit Open posture. The fixture now declares Secured alongside its bootstrap credentials; production authentication behavior is unchanged.
- The final Apps E2E rerun exposed an unstamped direct test-client read of bound behavior logs. The fixture now supplies the recipient's actual account/brain identity for that read and clears it afterward; the production bound-file guard remains enforced.
- Platform unit: 121 passed, including grant migration and source/scope denial.
- Apps unit: 98 passed (including missing/duplicate/unknown scenario results, legacy compatibility, pending recovery, and real row preservation across configure/upgrade/uninstall).
- CSharp unit: 77 passed; CSharp E2E: 2 passed, including sandbox restart.
- Postgres unit: 72 passed; 3 credential-gated live tests skipped.
- Assistant unit: 68 passed; E2E: 8 passed with the explicit secured authentication fixture.
- Supabase unit: 57 passed.
- Specs unit: 12 passed.
- IntoChat unit: 9 passed, including compilation of every shipped behavior and tests file.
- Apps E2E: 4 passed. IntoChat Customer Researcher install/open E2E: 1 passed after the shipped deterministic verification gate.
- Flutter shell: analysis clean; 118 tests passed.
- `aspire run --detach --isolated`: every running resource reported Healthy; the on-demand sandbox was NotStarted. The smoke AppHost was stopped afterward.

## Explicit limits

- The original generic `AGENT_FAILED` turn was not available to reproduce. The confirmed capacity-routing mismatch is addressed and future failures are correlated; this PR does not claim to identify that historical exception.
- Platform SQL still uses the configured database role's visibility. The new app-resource path enforces installation/brain/table ownership on every read, but does not redesign permissions for arbitrary legacy platform SQL.
- Scenario prose remains editable description compiled into ordinary C# behavior/tests, not a runtime natural-language interpreter. Individual cards expose their linked code and verification status; package verification remains the publication gate.
- Live external-model research is not part of the deterministic test gate.
