# Testing architecture implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans. The user approved implementation on 2026-09-21; do not request another design approval.

**Goal:** Make tests prove clear boundaries with native, short authoring APIs and physical feature folders.

**Architecture:** Modules own neuron unit tests, process/storage/HTTP integration tests, and applicable UI E2E tests. IntoChat owns only product E2E tests against its actual AppHost. The test framework owns host/browser startup and disposal; scenarios own seed data and model protocol fixtures.

**Tech stack:** Existing .NET 11, xUnit v3/Microsoft Testing Platform, Aspire, Playwright, Flutter and PostgreSQL. No new test DSL or duplicated application graph.

**Spec:** The agreed decisions, examples and migration map below are the implementation specification.

## Agreed decisions

| Boundary | Trigger | Authoritative assertion | Owner |
| --- | --- | --- | --- |
| Neuron logic | Typed operation | State/signals | Module Unit |
| Process/storage/HTTP/SSE | Adapter request or typed storage operation | Protocol/state/delivery | Module Integration |
| Reusable UI | Real browser gesture | Rendered behavior; typed setup allowed | Flutter E2E |
| Product workflow | Product action or typed composition | Connected product state/outcome | IntoChat E2E |
| Complete journey | Browser request through agent | Product result rendered | Small IntoChat E2E lane |

- IntoChat has one `IntoChat.Tests.E2E` project, physically `Tests/E2E`, grouped by Composition, Workspace, Agent and Inbox. No application integration project.
- Flutter projects live at `Tests/Unit`, `Tests/Integration`, `Tests/E2E`. Supabase tests use `Tests/Unit` and real-storage coverage where justified. Do not proliferate empty projects.
- `E2ETest.Create().WithModule<FlutterModule>().StartAsync(ct)` selects a module and uses module-owned E2E defaults (web plus headless browser).
- `E2ETest.For<AppHost>()` preserves the application's graph. Application overrides must not add modules or secretly apply selected-module defaults.
- `IntoChatE2ETest.StartAsync(ct)` / `Create()` supply explicit application defaults, backend only. `ConfigureModule<FlutterModule>(f => f.RunWebApp())` opts into automatic headless browser startup. `RunWebApp(browser => browser.Headed().SlowMo(250))` makes visibility explicit. Framework owns ready Page and cleanup.
- Public hosting names: `BackendOnly`, `RunWebApp`, `RunDesktopApp`. Obsolete Dart console host already removed; VM/DDS hot reload preserved.
- Data-to-UI tests directly create ISupabaseTable and open IWorkspace. They do not claim agent coverage. Browser setup creates/selects a project through real UI and captures workspace identity from its route.
- One application deployment owns temporary PostgreSQL. Scenario seeds it. No restricted-role fixture requirement; production protection remains unchanged.
- Agent tests retain production `/agent` entry and scoped scripted model protocol fixture. Paid/live model calls remain opt-in.
- Visible-only execution proves no UI behavior beyond explicit browser assertions. SlowMo controls browser calls, not backend timing.
- Keep one application startup smoke test. Generic composition configuration belongs in framework tests. Preserve product-specific isolation/cancellation/recovery guarantees.

## Global constraints

Work in the existing `archv2` checkout, preserving the nine-file console-host cleanup. No push. No unrelated production rewrite. Do not delete coverage because a file changes category. Do not fabricate windows in tests labeled agent journeys. Browser defaults cannot leak into production module settings. Keep credentials private. Module defaults are overridable.

## Review focus

1. Explicit BackendOnly survives module defaults, and actual AppHost configuration is preserved.
2. Browser startup failure/cancellation disposes acquired hosts and contexts.
3. UI tests do not rely on obsolete/noncompiled endpoints or a non-live gallery.
4. Moved projects retain bundle manifests, assembly references, namespaces, discovery and solution inclusion.
5. Agent fixtures do not silently use live providers; ordinary table scenarios start only one database.

## Responsibility and migration map

| Existing | Action / destination |
| --- | --- |
| IntoChatTestDeployment | Replace with small configuration-only IntoChatE2ETest; fail-closed unused providers; scenario protocol servers separately owned |
| AgentDataFixture | Delete broad deployment wrapper; scenario seed utility only; scripted model separately disposed |
| UiKitTwoWayWebFacts | Move/split into Flutter E2E feature tests; browser asserts rendering rather than rechecking every signal/neuron/HTTP assertion |
| UiKitWebFacts | Flutter E2E Rendering |
| SupabaseWorkspaceE2EFacts | Split direct data-to-UI Workspace scenarios from small Agent journeys and opt-in live test |
| AgentHttpFacts | Agent product outcomes, cancellation and replay; eliminate bundled role/database fixture |
| WorkspaceHttpFacts | Workspace product isolation and composed table contracts; keep guarantees |
| QueryWindowOperationFacts | Workspace product operation recovery; preserve restart/replay guarantees |
| ConfigurationFacts | Existing framework test project |
| ApplicationCompositionFacts | One application startup smoke; generic checks framework-owned where possible |
| HealthFacts | Composition/ApplicationStartupFacts |
| ElonInboxHttpFacts / ElonBitcoinUiFacts | Inbox product workflows |
| GmailWatchFacts | Module integration if only module endpoint acceptance, otherwise product flow |
| Production Agent/QueryWindowOperation.cs | Split durable operation contract/state/neuron from orchestration in cohesive Workspace/Queries folder |
| Tests root/project names | Physical move; update solution, build imports, IVT, CI and documentation |

## Task 1: Framework and Flutter hosting API

Files: `src/Testing/DigitalBrain.Testing.E2E/*`, relevant Hosting/Integration startup, `src/Modules/Flutter/Flutter/Configuration/*`, Flutter hosting, framework tests.

- [x] Pin default/override behavior and startup cleanup with meaningful framework tests; observe failure before implementation.
- [x] Add selected-module E2E startup reusing the module runner/bundle infrastructure, not a second composition system.
- [x] Provide module-owned test defaults without hardcoding Flutter in generic testing infrastructure.
- [x] Rename host APIs and provide browser configuration at the Flutter test call site. Keep browser settings process-local to testing.
- [x] Start browser automatically for declared browser endpoint; expose `brain.Page`; retain optional additional sessions only if useful.
- [x] Run framework test project and build module runner/E2E dependencies.

Expected authoring:

```csharp
await using var brain = await E2ETest.Create()
    .WithModule<FlutterModule>()
    .StartAsync(ct);
await using var visible = await IntoChatE2ETest.Create()
    .ConfigureModule<FlutterModule>(f => f.RunWebApp(b => b.Headed().SlowMo(250)))
    .StartAsync(ct);
```

## Task 2: Module test organization and browser coverage

Files: Flutter Tests/Unit, Tests/Integration, new Tests/E2E, Supabase Tests/Unit, existing module/hosting surfaces if essential for real UI tests.

- [x] Move physical directories and namespaces; fix relative project imports/references.
- [x] Move UI kit rendering/two-way coverage from IntoChat into Flutter E2E; target a real production Flutter route, not static fixtures.
- [x] Keep unit assertions in Unit, HTTP/neuron flow in Integration, browser outcomes in E2E.
- [x] Preserve module bundle manifest for browser project and use framework default web hosting.
- [x] Verify module unit/integration discovery and browser scenarios.

## Task 3: IntoChat product tests and fixtures

Files: `src/Applications/IntoChat/Tests/E2E/**`, IntoChat project IVT, framework ConfigurationFacts.

- [x] Move/rename application project and feature namespaces.
- [x] Replace deployment wrapper with explicit `IntoChatE2ETest.Create/StartAsync` returning native E2E builder/brain, same actual AppHost.
- [x] Use temporary PostgreSQL from application graph; seed via scenario-specific utility. Remove AgentDataFixture and restricted-role overhead.
- [x] Add direct typed table/workspace browser scenarios, keeping real UI project setup and focused render/interaction assertions.
- [x] Keep representative scripted/live agent journeys separate and preserve product isolation/cancellation/replay/failure checks.
- [x] Keep one startup smoke; move generic config checks and module-only tests to correct owner.
- [x] Run application test build and available real product scenarios.

## Task 4: Production folders, solution, CI, documentation and final review

- [x] Split QueryWindowOperation's contract/state/neuron/orchestrator into Workspace/Queries without behavioral changes; update references.
- [x] Update DigitalBrain.slnx and Foundation solution if affected; fix IVT and CI paths/discovery.
- [x] Ensure CI installs prerequisites for browser lanes and executes Flutter widget tests, with live tests excluded by default.
- [x] Record final physical tree, configuration ownership, migrations and commands/results here.
- [x] Run changed framework/module tests, solution build, Flutter tests and representative E2E; report environmental blockers rather than claiming unrun checks pass.
- [x] Independent review of diff against this plan; resolve material findings and verify affected tests.

## Explicit non-goals

Database mutation features, new typed agent submission contract, replacement application composition graph, new general-purpose test DSL, rewriting production authorization, broad cleanup of historical excluded HTTP files, publishing or pushing changes.

## Execution record

- Console host cleanup before this plan: 57 framework tests passed; hot reload unchanged.
- Plan authorized by user: "form the plan and implement now". Remaining routine choices will be documented rather than prompting for further approvals.

### Implemented structure and migration decisions

- Flutter `Tests/Unit`, `Tests/Integration`, `Tests/E2E`; Supabase `Tests/Unit`.
- IntoChat `Tests/E2E/{Composition,Workspace,Agent,Inbox}` and assembly `IntoChat.Tests.E2E`.
- Selected-module defaults: `IModuleE2EDefaults<TModule>`, implemented by Flutter; browser callback overload in the separate `DigitalBrain.Modules.Flutter.Testing` adapter.
- Actual AppHost defaults: `IntoChatE2ETest.cs`; direct table scenarios seed the same app's PostgreSQL through `LeadData`, then use `ISupabaseTable` + `IWorkspace`.
- Production `Workspace/Queries` splits unchanged durable contracts, neuron and orchestration. Existing aliases remain stable.
- GmailWatchFacts was removed because Google module Integration already verifies the same webhook acceptance and MailReceived signal. No product-specific wiring was asserted there.
- ApplicationCompositionFacts inventory assertion mirrored AppHost declarations; removed. Port isolation is framework-owned, as approved; the actual app keeps one health/startup smoke. Excel/Flutter identity and module override configuration assertions moved to framework tests.
- UI kit browser tests reuse the existing live InboxBanner. No new component gallery or production route is introduced.
- Old mixed SupabaseWorkspaceE2EFacts became direct Workspace data/render/restore scenarios and separate Agent scripted/live journeys. Browser agent failure/retry coverage is retained alongside cancellation.
- CI installs Chromium and Flutter, serializes test modules to avoid shared Flutter build contention, runs widget tests, and defaults live calls off. No Foundation solution exists in this checkout; its preexisting workflow reference was not part of this migration.

### Validation

- Framework tests: 65 passed; framework integration: 7 passed, including concurrent ports and restart durability.
- Flutter unit: 34 passed; Flutter integration: 25 passed; Supabase unit: 9 passed.
- Flutter shell full suite: 14 passed; UI kit widgets: 4 passed.
- IntoChat build passed. Actual startup smoke: 1 passed. Backend product scenarios: 6 passed (agent protocol/error/cancellation/replay, workspace data/isolation and durable query recovery, webhook inbox).
- Full solution Debug and Release builds passed with 0 warnings/errors. An earlier Debug build collided with running test executables; the later build passed after those exited.
- Framework independent review found no actionable issue. Product review requested retaining rendered agent failure/retry coverage (added). Its suggestion to restore concurrent actual-product deployments was declined to respect the explicitly approved single app smoke/framework ownership boundary.
- All migrated browser scenarios pass across focused runs. Live model opt-in was checked: 1 test skipped by default; paid live calls are not run.

- Whole-solution whitespace verification reports existing formatting/end-of-line issues in many unchanged files. Changed C# files are formatted separately; this work does not expand into repository-wide formatting cleanup.
- Initial product browser run: 3 passed (direct data/filter, empty/unavailable source, webhook inbox), 2 failed on overly exact Flutter accessible-name selectors, 1 live test skipped. Trace snapshots showed the message label gains its hint on focus and a project card label includes conversation/item counts. Locators now match their stable label portion. Direct workspace reopen uses Project files, not an agent result link; targeted rerun follows.
- Changed-source whitespace verification passed with explicit file includes. Independent module review found no actionable issues after reusing InboxBanner.

- Flutter browser suite final status: all 3 scenarios passed across focused runs (Button, Expander, Rendering). Rendering's final targeted run passed 1/1 in 1m26s.

### Final result

Implementation complete. The actual source and API guide is `src/Testing/README.md`.

| Suite | Passed |
| --- | ---: |
| Framework | 65 |
| Framework Integration | 7 |
| Flutter Unit | 34 |
| Flutter Integration | 25 |
| Flutter E2E | 3 |
| Supabase Unit | 9 |
| IntoChat E2E | 12 |
| Flutter shell | 14 |
| Flutter UI | 4 |

155 .NET and 18 Flutter tests passed across focused runs; one paid live-model test was confirmed skipped by default. Final product agent run covers successful query/table display, cancellation, visible failure, retry clearing the previous failure, and composer recovery. Final workspace run covers inactive-workspace isolation, filtered view reload, close, and reopening through the real Project files button.

Final targeted browser evidence: `artifacts/testing-redesign/product-browser-final.log` (agent passed; an older workspace locator failed in that run), `workspace-restore-final.log` (corrected workspace passed 1/1), and `module-rendering-rerun.log` (corrected rendering passed 1/1). Earlier product browser run supplied the other three passing product cases. No browser failure remains unresolved.

Debug and Release solution builds passed with zero warnings/errors. Changed-source formatting and `git diff --check` pass. Whole-repository formatting still reports preexisting issues in untouched sources; no unrelated formatting cleanup was made. Linux CI and paid live providers were not executed locally. The user subsequently requested a local commit; no push is requested.

- Follow-up: removed the empty Fixture.slnx and its output-copy entry. IntoChat E2E defaults explicitly clear CodingModule.SolutionPath, so workspace warmup remains inactive. Restored Composition/ApplicationStartupFacts.cs after its accidental move into Inbox.
- Follow-up verification: IntoChat E2E Release build passed; restored startup smoke passed 1/1 with CodingModule.SolutionPath cleared. Staged whitespace check passed.
