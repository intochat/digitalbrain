# Testing Framework Refactoring Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline execution, or superpowers:subagent-driven-development if the user selects delegation. Execute tasks in order and track steps with checkboxes.

**Goal:** Make unit, module integration and application E2E tests simple to configure consistently while preserving deep module settings, provider substitution, process isolation and optional visible browsers.

**Architecture:** Reuse production `ModuleDefinition` and `ModuleComposition` as the authoritative composition seam. Retain separate test entry points and concrete brain capabilities, with shared execution/lifetime contracts. Keep Aspire orchestration and browser driving out of the common testing package.

**Tech Stack:** Existing .NET 11 SDK, Orleans, Aspire 13.5.3, Playwright 1.62.0, Flutter and xUnit/Microsoft.Testing.Platform; no upgrades in this work.

**Spec:** [Testing unification review](../../research/2026-09-20-testing-unification-review.md), refined by the concrete decisions below. [Primary sources](../../research/2026-09-20-testing-primary-sources.md). This plan replaces the execution sequence of the earlier testing plan only after approval.

**Status:** Approved by the user and implemented on `codex/testing-refactoring`. See the [execution ledger](testing-refactoring-progress.md) for validation, consolidated commit sequencing and the approved infrastructure-retention fallback. Packaging/native-asset/Linux gate items remain unchecked because those capabilities were not established; the programmatic prototype and pack attempts failed before external-consumer validation.

## Global constraints

- Preserve existing uncommitted changes; never reset the workspace to the older design.
- Public testing packages remain `DigitalBrain.Testing`, `.Unit`, `.Integration`, `.E2E`.
- Unit means the existing in-process neuron/component harness; ordinary unit tests need no harness.
- Integration retains a separate runtime process and production HTTP/Orleans bootstrap.
- E2E starts the application's actual AppHost. HTTP-only E2E is supported.
- Every brain implements production `IDigitalBrain`; keep native Playwright `IPage` access.
- Production configuration contracts cannot depend on testing, xUnit, Aspire or Playwright.
- No live external accounts by default; no automatic retries of scenario actions.
- No resolved secrets in command-line arguments, public configuration snapshots or diagnostic payloads.
- Use the pinned SDK and `-p:CodeGraphRefresh=false` for build/test commands.
- All new API code below is an implementation target, not an existing interface.

## Decisions proposed for approval

### Configuration and public API

Keep three entry points, each receiving one options object plus cancellation. Use module definitions consistently in Unit and Integration. E2E receives typed application configuration through a small production interface, not an arbitrary list that is silently treated as settings.

```csharp
// Production composition contract, no testing dependency.
public interface IApplicationConfiguration
{
    IReadOnlyList<ModuleDefinition> Modules { get; }
}

// DigitalBrain.Testing.E2E
public sealed record E2EOptions
{
    public required IApplicationConfiguration Application { get; init; }
    public TestExecutionOptions Execution { get; init; } = new();
    public BrowserOptions Browser { get; init; } = new();
}

Task<UnitBrain> UnitTest.StartAsync(UnitOptions options, CancellationToken ct = default);
Task<IntegrationBrain> IntegrationTest.StartAsync(IntegrationOptions options, CancellationToken ct = default);
Task<E2EBrain> E2ETest.StartAsync<TAppHost>(E2EOptions options, CancellationToken ct = default)
    where TAppHost : class;
```

The deleted `IApplicationConfiguration.cs` in the working tree must be inspected before adding the new minimal contract: reuse its location, not its former larger design. Snapshot `Application.Modules` once before asynchronous startup. Do not serialize the interface or infer keys from property names.

`UnitOptions.Modules` becomes `IReadOnlyList<ModuleDefinition>`. Retain local `ConfigureSilo`, `ConfigureClient` and `UseReminders`, and add `Execution`. Migrate `new Module()` callers to module-owned `Define` factories where present, otherwise `new ModuleDefinition(typeof(Module))`. Do not require empty option types for settings-free modules.

The application owns its module slots. A supplied application snapshot replaces the complete settings of those slots; validate module identities against the application's declared default composition. Reject missing/unknown slots before launching resources. Integration remains the way to select arbitrary module subsets. Optional application modules require an explicit application feature, not a testing backdoor.

### Defaults and execution preferences

Preserve normal application defaults, including Flutter Window. Remove E2E's hidden switch from Window to Web. A UI test explicitly selects Web; an HTTP-only E2E test explicitly selects None. Require `Application` in E2E setup, eliminating ambiguity between omitted configuration and a default object.

```csharp
await using var brain = await E2ETest.StartAsync<Projects.IntoChat_AppHost>(new()
{
    Application = new DigitalBrainConfiguration
    {
        Flutter = new() { Hosting = new() { Kind = FlutterHostKind.Web } },
    },
    Browser = new() { Headless = false, SlowMoMilliseconds = 250 },
}, ct);
await using var browser = await brain.OpenBrowserAsync(ct);
```

Browser settings belong exclusively in E2E. Use nullable `Headless` and `SlowMoMilliseconds` overrides, then resolve once at startup: explicit value > existing environment/debugger preference > headless with zero slow motion. Preserve `DIGITALBRAIN_E2E_HEADED=1` for local observation. Explicit zero slow motion and explicit headless must win. Do not derive either from Flutter hosting kind. One brain owns one browser engine configuration; remove the per-open launch-options overload after caller migration.

Common execution defaults remain startup 3 minutes, assertion 5 seconds, cleanup 30 seconds. E2E browser defaults are startup 2 minutes and UI action/assertion 60 seconds, exposed separately in BrowserOptions. Generic assertions retain native per-call overrides. No global mutable timeout settings.

### Flutter browser preparation

Generic E2E opens the advertised endpoint and waits for document readiness. Extend the existing browser endpoint annotation with a relative navigation path/query and optional readiness selector. Flutter advertises its semantics tree selector and the application's existing `semantics=true` URL parameter through that metadata. Backend/session-specific readiness remains the scenario's responsibility. Use a DOM selector string in the endpoint metadata, not a Playwright delegate or a new testing package in the Flutter module.

The annotation therefore carries endpoint name, relative navigation path/query, and optional readiness selector. Generic startup can wait for that selector without knowing Flutter tags. Flutter declares `flt-semantics`; retain its existing programmatic semantics activation. This proves accessibility initialization only, not authentication, subscription or data readiness. Do not add a new frontend marker or interop subsystem when the existing semantics tree already provides the required signal.

### Project structure

The initial deliverable has four public libraries under `/Testing/`, with `/Testing/Infrastructure/` and `/Testing/Tests/` solution subfolders. Keep physical paths unchanged initially to avoid unrelated MSBuild path churn. Make implementation types internal where possible; retain necessary cross-assembly access without presenting them as user APIs.

Attempt to eliminate ModuleAppHost using Aspire's programmatic test builder only after proving SDK/runtime metadata and package behavior. If that fails the explicit acceptance gate, retain it and record the reason. ModuleRunner stays a child-process tool. Shared Hosting stays a dependency of Integration and E2E; moving it into common Testing would burden Unit with Aspire, while moving it into Integration would couple E2E to the module runner.

**This approval covers four public packages, not a promise of four total project files.** Framework test projects remain separate. A strict four-physical-project redesign would need a different approved trade-off.

## Review focus

1. Explicit `Headless=true` and `SlowMoMilliseconds=0` must survive a headed environment/debugger preference (Task 1).
2. A dependency selected only transitively must register its services exactly once in both unit and hosted execution (Task 2).
3. A partial or unknown application module snapshot must fail before launching processes instead of borrowing ambient settings (Task 3).
4. Cancellation during navigation or failing artifact capture must still close owned browser resources and preserve the original failure (Task 4).
5. A selected provider adapter must load in the external runtime, and two runs must not share provider state or durable storage (Tasks 5–6).

## Task 0: Preserve the baseline and establish the implementation checkout

**Files:** Existing modified application/testing files reported by `git status`; research notes and this plan.

**Interfaces:** Consumes the current working tree; produces an isolated implementation baseline preserving its contents.

- [x] Read local instructions and the research evidence; record `git status --short` and current commit.
- [x] Use the worktree skill at execution time. Preserve the existing dirty-tree content explicitly; native worktree creation does not copy it. Never commit unrelated changes or silently start from HEAD without the refactoring changes.
- [x] Record the current targeted failures with the commands in Validation. The two-way test currently fails with None/no endpoint; the web sample reproduces the semantics-placeholder race.

## Task 1: Repair browser startup and honor execution preferences

**Modify:** `src/Testing/DigitalBrain.Testing.E2E/{E2EBrain,E2ETest,BrowserSession}.cs`, `src/Testing/DigitalBrain.Testing/{BrowserOptions,TestExecutionOptions}.cs`, `src/Applications/IntoChat/Tests/UiKitTwoWayWebFacts.cs`.

**Create:** `src/Testing/DigitalBrain.Testing.E2E/BrowserOptions.cs`, `BrowserOptionsResolver.cs`; tests `BrowserOptionsFacts.cs` and `BrowserReadinessFacts.cs` under the existing framework test projects as appropriate. Add E2E references only to tests that need them.

**Interfaces:** Produces nullable browser overrides and an internal resolved launch value. Common `TestExecutionOptions` no longer contains Browser. This task may temporarily retain Flutter initialization inside E2E until Task 4 extracts metadata-based readiness.

- [x] Add pure precedence cases using an internal resolver that takes environment/debugger values as parameters, avoiding mutation of process-wide settings in parallel tests:

```csharp
var resolved = BrowserOptionsResolver.Resolve(
    new() { Headless = true, SlowMoMilliseconds = 0 },
    headedRequested: true, debuggerAttached: true);
Assert.True(resolved.Headless);
Assert.Equal(0, resolved.SlowMoMilliseconds);
```

- [x] Cover omitted preferences, explicit headed, negative/nonfinite slow motion, and invalid browser timeouts. Run the failing cases before implementation.
- [x] Remove the count-then-click placeholder path. Keep the current application semantics opt-in and await the semantics tree with the remaining browser startup budget.
- [x] Restore Web in the two-way UI test, resolve browser options once, and propagate the resolved values unchanged to launch.
- [x] Run both real UI tests. A passing synthetic DOM test alone is insufficient evidence for this bug.
- [x] Commit only this coherent fix and its tests after verification.

## Task 2: Make resolved module composition authoritative

**Modify:** `src/Modules/DigitalBrain/DigitalBrain/Composition/{ModuleDefinition,ModuleComposition}.cs`, `src/Testing/DigitalBrain.Testing.Unit/{UnitOptions,UnitTest}.cs`, `src/Modules/DigitalBrain/Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs`, `src/Testing/DigitalBrain.Testing.ModuleAppHost/AppHost.cs`.

**Tests:** Extend `src/Testing/DigitalBrain.Testing.Framework.Tests/CompositionFacts.cs`; add dependency-registration coverage in `src/Modules/DigitalBrain/Tests/ModuleCompositionFacts.cs`.

**Interfaces:** `ModuleComposition.Resolve(IReadOnlyList<ModuleDefinition>)` remains the resolver. Add production `AddModules(IReadOnlyList<ModuleDefinition>)` on `DigitalBrainBuilder`; it resolves once, applies definition settings and registers matching hosting/runtime modules. Existing `AddModule<T>` remains for production callers; do not invent a universal options registry.

- [x] Add a dependency module that registers a marker service and a dependent module that requires it; select only the dependent module. Verify the actual started unit host resolves the marker.
- [x] Assert duplicate equal definitions configure once and conflicting definitions fail before cluster startup. Exercise conflicting configuration keys across distinct definitions rather than silently accepting order-dependent values.
- [x] Change Unit startup to configure the resolved list, not the original list. Reject configuration overriding harness-owned storage/identity sections consistently across hosted paths.
- [x] Wire the shared production AddModules path into ModuleAppHost; preserve its child runtime launch and dependency bundle.
- [x] Migrate unit callers from IModule instances to definitions. Retain local callbacks for controlled providers; verify representative Time, Google and Flutter tests.
- [x] Commit composition and migrated callers together after tests pass.

## Task 3: Introduce consistent options and typed application configuration

**Create:** `src/Modules/DigitalBrain/DigitalBrain/Composition/IApplicationConfiguration.cs`, `src/Testing/DigitalBrain.Testing.E2E/E2EOptions.cs`.

**Modify:** `src/Applications/IntoChat/Configuration/DigitalBrainConfiguration.cs`, `src/Applications/IntoChat/AppHost/AppHost.cs`, `src/Testing/DigitalBrain.Testing.E2E/E2ETest.cs`, `src/Modules/Flutter/Flutter/Configuration/FlutterModuleOptions.cs`, `src/Modules/Flutter/Flutter/FlutterModule.cs`, `src/Modules/Flutter/Aspire.Hosting/{ShellHostingExtensions.cs,Configuration/FlutterHostOptions.cs}` and application test callers.

**Create:** Production `ApplicationConfigurationTransport.cs` beside the composition contracts. It uses a versioned public-settings envelope and module identities; it does not serialize arbitrary objects or secret values. Add binder/round-trip tests under framework tests and IntoChat configuration tests in the application test project.

**Interfaces:** `DigitalBrainConfiguration : IApplicationConfiguration`; application-owned `Bind(IConfiguration)` and default configuration share module factories. `ApplicationConfigurationTransport.Write(IApplicationConfiguration)` returns a string envelope; `Read(string, IReadOnlyList<ModuleDefinition> allowedModules)` validates and returns resolved definitions. Transport the envelope through a reserved `DigitalBrain:Testing:Application` setting; reject module settings attempting to set that section. Limit the UTF-8 envelope to 8 KiB and fail before launch if exceeded; configuration contains settings, never test data payloads. Resolve module identities only against the supplied allowlist, not arbitrary type names from the envelope.

- [x] Add round-trip tests for a non-default Flutter shell/chat and Google PublicOrigin. Assert unknown module identities, missing slots, unsupported envelope versions and forbidden harness keys fail before Aspire startup.
- [x] Add default-equivalence tests: ordinary app binding and `new DigitalBrainConfiguration()` select the same defaults; E2E must not secretly change them.
- [x] Make application binding and definition factories own validation/key mapping. Consolidate Flutter option definitions so typed public settings cover existing host configuration; keep device/process launch mechanisms in the Aspire adapter.
- [x] AppHost either binds normal configuration or consumes the validated complete application envelope. Build its graph from those definitions through AddModules; remove duplicated hard-coded selection and partial remapping.
- [x] Implement the E2EOptions overload, migrate every E2E caller, then remove the module-list and implicit-application overloads. Native browser session creation remains explicit.
- [x] Verify setting values reach the runtime/hosting adapter, not just the transport DTO. Assert no supplied application field is silently ignored.
- [x] Commit the contract, binding and caller migration together after validation.

## Task 4: Share budgets/lifetime and separate frontend readiness

**Modify:** `src/Testing/DigitalBrain.Testing/{TestExecutionOptions,ITrackedBrain,BrainTestExtensions}.cs`, `Observation/{SignalProbe,BehaviorRun,TestWait}.cs`, `Lifetime/TestSessionLifetime.cs`, UnitBrain, HostedBrain, AspireTestSession, E2EBrain and BrowserSession.

**Modify:** `src/Modules/DigitalBrain/Aspire.Hosting/Brain/BrainEndpointAnnotation.cs`, Flutter ShellHostingExtensions, `src/Modules/Flutter/app/shell/lib/main.dart`.

**Create:** Framework tests `ExecutionBudgetFacts.cs`, `BrowserLifecycleFacts.cs`; extend existing Flutter shell tests only if its semantics initialization changes.

**Interfaces:** Tracked brains supply per-run execution settings to observation helpers. SignalProbe and BehaviorRun receive budgets at construction; ordinary IDigitalBrain callers keep documented defaults. Browser endpoint metadata adds navigation path/query and optional readiness selector. BrowserOptions adds StartupTimeout and AssertionTimeout.

- [x] Add a real no-signal probe test with a short per-run assertion timeout; verify it expires under that budget rather than the fixed five seconds. Run two differently configured sessions to detect global leakage.
- [x] Use a shared lifetime owner in UnitBrain; preserve disposal order, rollback on failed startup, aggregation of cleanup failures and idempotent disposal. Nested owners receive a common absolute cleanup deadline rather than starting fresh full budgets.
- [x] Apply one startup deadline to each startup operation. Browser launch/navigation/preparation share the browser deadline; cancellation closes the context so pending Playwright calls terminate.
- [x] Have Flutter's hosting annotation advertise the navigation query and `flt-semantics` selector. Retain programmatic semantics initialization in the shell. Remove all Flutter-specific tags/query knowledge from generic E2E.
- [x] Test metadata-driven navigation with a tiny local HTML endpoint, then verify the real Flutter shell. Keep application login/data-readiness assertions in application tests.
- [x] Capture screenshot and trace independently; ensure one failure does not skip the other. Preserve original startup/action exceptions while attaching cleanup/artifact diagnostics. Bound console/page-error/request summaries and omit sensitive payloads.
- [x] Cover cancellation during navigation, failed screenshot capture, repeated disposal and session cleanup after browser preparation fails.
- [x] Commit after unit/lifecycle tests and real UI smoke tests pass.

## Task 5: Make external provider overrides typed and process-safe

**Modify:** `src/Modules/Google/Google/{GoogleModule.cs,Configuration/GoogleModuleOptions.cs}` and `src/Modules/Google/Tests/Integration/GmailOAuthCallbackFacts.cs`; application configuration binding where necessary.

**Tests:** Existing Google token exchange fake tests and OAuth callback integration test; add a small test support provider module to the framework integration test assembly for non-network replacement coverage.

**Interfaces:** Add typed non-secret `TokenEndpoint` to GoogleModuleOptions and map it to the existing provider setting. Credential handling remains outside public module definitions. Local ConfigureSilo callbacks do not cross processes.

- [x] Convert the existing token endpoint stub case to typed Google options. Verify the stub receives the exchange and the neuron emits GmailConnected.
- [x] Supply synthetic OAuth credentials through a private test-owned configuration channel, not module arguments. Use a restricted run-scoped file with an environment reference, delete it on rollback/disposal, and test diagnostic redaction. Do not add support for live account testing.
- [x] Retain in-process interface replacements with existing fake providers. Use explicit DI replacement where registration order otherwise changes behavior.
- [x] Demonstrate a compiled test support module registering a provider inside the external child. Assert external PID and actual provider behavior. Do not add generic object serialization or remote delegate execution.
- [x] Run the provider scenarios twice with isolated instances and verify no state crosses runs.
- [x] Commit typed options, fixtures and provider transport tests together.

## Task 6: Reduce the exposed project surface and prove packaging

**Modify:** `DigitalBrain.slnx`, testing csproj references, `src/Testing/Integration.Tests.props`, `DigitalBrain.Testing.Integration/buildTransitive/DigitalBrain.Testing.Integration.targets`, ModuleBundle and IntegrationTest.

**Tests:** `src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/{BundleFacts,RunnerLoadingFacts,IsolationFacts}.cs`.

**Interfaces:** Four public packages remain. Shared hosting and runner are implementation dependencies. If the gate passes, Integration constructs the module resource graph programmatically and ModuleAppHost is deleted; consumers still call IntegrationTest.StartAsync unchanged.

- [x] Group public packages, infrastructure and framework tests separately in the solution. Update misleading package descriptions; restrict implementation visibility where possible.
- [ ] Prove current bundle behavior: selected module absent from runner static references, transitive assembly, representative native asset, separate host adapter, external PID, restart persistence and parallel isolation. Add missing cases before changing packaging.
- [ ] Prototype programmatic Aspire graph creation within Integration using the pinned SDK support. Verify required `dcpclipath` and runtime assets are available outside the repository checkout.
- [ ] Pack into a temporary local feed and consume from a minimal external test project with one module. Build, start, call its endpoint, restart, then dispose. Repeat on Windows and Linux CI.
- [x] Delete ModuleAppHost only if all checks pass without consumer-specific generated projects, runtime builds, duplicated graphs or hidden references to source-tree paths. Otherwise retain it, remove the prototype, and document the exact failed criterion. This fallback is part of the proposed approved plan.
- [x] Keep the external runner and framework tests. Do not merge them into production/runtime projects to satisfy a cosmetic count.
- [x] Commit organization separately from a successful packaging deletion so reviewers can assess each independently.

## Task 7: Finish caller migration, documentation and acceptance

**Modify:** All remaining testing callers found by `rg`, `src/Testing/README.md`, old testing spec/plan status headers, applicable module examples and project references.

**Interfaces:** Only the finalized entry points remain; no permanent compatibility wrappers for removed in-repository APIs.

- [x] Search for `TestExecutionOptions.*Browser`, obsolete E2E overloads, IModule lists in UnitOptions, raw provider endpoint keys, placeholder clicks and fixed framework timeout use. Classify legitimate low-level cases rather than replacing blindly.
- [x] Document one neuron test, one module HTTP test and one actual application UI test with deep typed settings. Explain Flutter None/Headless/Web/Window versus browser headed/headless.
- [x] Document explicit precedence, configuration defaults, provider substitution, process guarantees, cancellation, artifacts and package support requirements.
- [x] Run the acceptance matrix below, then the relevant complete suites. Run the solution suite once as the final broad gate; investigate failures and distinguish existing unrelated failures with evidence. Executed coverage and platform/package exclusions are recorded in the ledger.
- [x] Review the final diff against every approved decision. Record any deviations and measured results. Do not claim a four-project result if internal projects remain.

## Validation commands and acceptance matrix

Run from repository root. Use the pinned Microsoft.Testing.Platform runner and confirm filters select nonzero tests.

```powershell
dotnet test --project src/Testing/DigitalBrain.Testing.Framework.Tests/DigitalBrain.Testing.Framework.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/DigitalBrain.Testing.Framework.IntegrationTests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Flutter/Tests.Unit/DigitalBrain.Modules.Flutter.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Flutter/Tests.Integration/DigitalBrain.Modules.Flutter.Tests.Integration.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Applications/IntoChat/Tests/IntoChat.Tests.csproj -p:CodeGraphRefresh=false -- --filter-method '*NeuronChangeShowsInUiAndTapUpdatesNeuron*'
dotnet test --project src/Applications/IntoChat/Tests/IntoChat.Tests.csproj -p:CodeGraphRefresh=false -- --filter-class '*UiKitWebFacts'
dotnet test --solution DigitalBrain.slnx -p:CodeGraphRefresh=false
```

Locate actual Google/Time project names with `rg --files src/Modules/Google src/Modules/Time -g '*.csproj'` and execute their unit/integration suites after their respective migrations. Flutter shell readiness tests use the repository's existing Flutter test workflow; resolve current CLI documentation before changing that workflow.

| Scenario | Required result |
|---|---|
| Transitive module selection in Unit and Integration | Dependency configured once; required service available |
| Complete application snapshot with non-default leaves | Same values consumed by Aspire and runtime |
| Invalid/conflicting/missing application modules | Fail before resource launch with actionable error |
| Web + explicit headless | Full two-way UI test passes without window |
| Web + explicit headed, slow motion 250 | Same full two-way UI test passes visibly |
| Explicit headless/zero slow motion with headed environment | Explicit values win |
| None + browser request | Immediate configuration error, no locator timeout |
| Pure-Dart Headless | Existing host behavior preserved; never treated as web browser mode |
| UI semantics initialization | No dependency on placeholder survival |
| Cancellation/failed browser preparation | Owned context/processes released; original error retained |
| Runtime restart | Committed state preserved within run; observations reacquired |
| Two independent runs | State, endpoints, provider fixtures and resources isolated |
| External package consumer | Assets load and cleanup works without repository paths |

Repeat the two-way UI scenario three times each in headless/zero and headed/250 modes after the readiness fix. These are independent verification runs, not automatic retries that turn a failing run green. Keep all failures and artifacts.

## Approval and execution

Approval accepts the API/default decisions, ordered tasks and conditional ModuleAppHost-removal gate above. Recommended execution is inline in this task because composition, binding, browser startup and packaging share interfaces and should change sequentially. A final independent review can follow implementation; choose delegated execution only if desired.

User approval was received before implementation. No product source was changed while preparing the original plan. See the execution ledger for the resulting changes and verification.
