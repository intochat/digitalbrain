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

- [ ] Test that two behavior files in one installed app share identity, standalone files retain identity, and untrusted callers cannot bind arbitrary app IDs.
- [ ] Add a host-only app/file execution binding validated against the originating Apps grain; persist and return it with run authorization. Stamp verified identity at the script edge.
- [ ] Deploy behaviors with the installed app identity. Carry migration history from all recorded legacy file scopes.
- [ ] Migrate table ownership without moving/renaming physical tables or changing pinned capacity origin; preserve retry safety and reject unrelated owners.
- [ ] Retire both stable app storage and historical file storage on uninstall; verify configuration/upgrade does not lose rows.
- [ ] Run CSharp, Apps, and Postgres unit suites plus relevant E2E tests.

### 2. Authorized data discovery and assistant diagnostics

Affected: `Modules/Postgres` tool factory, source/provider selection and table resource discovery; `Modules/DigitalBrain/Assistant` instructions/tool policy/error reporting and tests; Apps installation listings as authorization evidence; Registry only if actual resource discovery requires it.

- [ ] Reproduce routing mismatch with a test where table capacity is not the platform database.
- [ ] Expose discoverable app table resources with installation identity and pinned origin through a host-authorized path; preserve membership and ownership checks.
- [ ] Let assistant table tools select a discovered resource explicitly and route schema/read windows through its real origin. Preserve existing platform Postgres behavior.
- [ ] Retain actionable errors and correlation in server logs; do not expose secrets or replace failures with successful-looking results.
- [ ] Verify unavailable sources, cross-brain denial, legacy platform tables and provisioned tables.

### 3. Scenario-first UI and marketplace

Affected: Flutter shell `workspace/apps` screens, authoring document models/editors, scenario/verification display, marketplace cards and widget tests; Apps authoring contracts/codec/prompts only if needed for stable scenario bindings.

- [ ] Make each scenario a visible standalone block with trigger/outcome prose, linked source and test status. Preserve stable IDs while editing/duplicating/reordering.
- [ ] Keep behavior source mappings compatible but show scenarios as the primary authoring unit, including shipped packages without metadata.
- [ ] Present marketplace identity as `owner/name`; clearly distinguish published catalog entries and installed apps, with search and existing install/fork/spec actions.
- [ ] Add widget coverage for scenario cards, source links, legacy documents, verification status and namespaced marketplace entries.
- [ ] Run Flutter analyze and shell tests.

### 4. Thin researcher scenarios and test support

Affected: `IntoChat/Apps/CustomerResearcher` manifest/spec/behaviors/tests; Apps client/testing support and authoring prompts; shipped-content and researcher E2E tests. Specs module receives only changes actually needed for compatibility/documentation, rather than a duplicate execution framework.

- [ ] Share invocation/subscription plumbing and scratch-install test lifecycle in small reusable helpers available to scripts.
- [ ] Keep scenario handlers thin: open UI, submit research, run research, stop, inspect results. UI buttons and app operations converge on the same invocation path.
- [ ] Keep the bounded browser/model research algorithm ordinary code; do not fragment its internal method calls into workflow steps.
- [ ] Use stable scenario IDs for verification where metadata exists, retaining name-based compatibility for old packages.
- [ ] Update authoring guidance to produce small trigger/outcome scenarios and use reusable test plumbing.
- [ ] Verify deterministic researcher behavior, refusal, cancellation, saving and reading data; compile live scenarios when external services are unavailable.

### 5. Integration, review and PR

- [ ] Run affected C# unit and E2E projects, Flutter analyze/tests, IntoChat composition checks, and attempt Aspire smoke.
- [ ] Review all changes for privilege escalation, storage loss, retry/restart behavior, stale verification, and mismatched Dart/C# contracts.
- [ ] Record actual results and limitations below, commit on `codex/app-scenarios-identity`, push and create a PR. Do not merge.

## Review focus

1. A script must not impersonate another app via settings or caller inheritance.
2. Upgrade/reconfigure and interrupted migrations must preserve existing rows and origin.
3. An assistant must not discover/query another brain's app data through platform SQL privileges.
4. Renaming/reordering scenarios must not attach another scenario's test result.
5. Legacy shipped packages without authoring metadata must remain readable, runnable and verifiable.

## Execution record

- Initial checkout clean on `master`; created `codex/app-scenarios-identity` in the existing checkout as requested.
- Ruling: user explicitly requested document then implementation and PR; proceed without another design-approval round.
- Ruling: use existing C# behaviors and signals for scenario execution; earlier fluent API examples were sketches, not a mandate to introduce a competing workflow language.
- Shared interfaces: identity task owns Postgres ownership/lifetime files; data task owns Postgres tools/providers/discovery files. Coordinate any new shared contracts before editing.
- Shared interfaces: UI keeps existing authoring document contracts unless coordinated; researcher work supplies structured metadata matching those contracts.

## Validation and remaining limitations

Pending implementation and verification.
