# DigitalBrain Testing Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. The user has requested a plan, not implementation; no execution method has been selected.

**Goal:** Deliver four coherent testing packages with explicit per-test lifetimes, typed composition, one external module runner, and actual-AppHost E2E tests.

**Architecture:** Share observations and lifecycle primitives, while keeping unit hosting, module integration hosting, and application E2E composition separate. Module integration launches one reusable production-runtime runner; E2E launches the real application. Both use common Aspire ownership and connection machinery, without either depending on the unit framework.

**Tech Stack:** Existing pinned .NET 11 prerelease SDK, Orleans, Aspire, xUnit v3 on Microsoft.Testing.Platform, Playwright, Azure Storage providers with Azurite for local integration.

**Spec:** [Testing framework design](../specs/2026-09-20-testing-framework-design.md). Read both documents before implementation.

## Global constraints

- Public packages are `DigitalBrain.Testing`, `DigitalBrain.Testing.Unit`, `DigitalBrain.Testing.Integration`, and `DigitalBrain.Testing.E2E`.
- Use explicit asynchronous startup and `await using` in tests. No test base classes, custom xUnit runner, or automatic fixture sharing in the initial implementation.
- Integration tests select modules and use one shared external module runner with Aspire-managed infrastructure. No AppHost project per module and no dependency on IntoChat.
- E2E tests start the application's actual AppHost with application-owned typed options. No named IntoChat test profiles.
- Preserve pinned SDK `11.0.100-rc.1.26425.128`, target `net11.0`, and `xunit.v3.mtp-v2` version `4.0.0` unless an independently justified compatibility issue requires review.
- Preserve existing user edits and untracked files. Do not broadly stage, reset, or regenerate unrelated Flutter files.
- No test-time build/restore, signal replay, action retries, or transport of arbitrary DI delegates into external processes.
- New configuration/hosting contracts used by production live in production projects, never in test packages.
- Framework internals remain independent of xUnit. Test projects alone consume its cancellation/output facilities.

## Review focus

1. Module bundles with incompatible shared assembly versions or native assets must fail descriptively or resolve correctly, not hang at health checks: Tasks 1 and 5.
2. Canceled or partially completed startup must release all owned resources, even if cleanup itself fails: Tasks 3 and 6.
3. Concurrent runs and restart-within-one-run must preserve the correct isolation/persistence distinction: Tasks 6 and 7.
4. Behavior retries, multiple subscriptions, and stale readiness from an older run must not permit premature webhook delivery: Task 8.
5. False/empty option values, missing credentials, ambiguous endpoints, and failed artifact collection must not silently change configuration or prevent cleanup: Tasks 4, 9 and 10.

## Working-tree baseline and execution rules

This plan was authored against the modified working tree on `codex/framework-foundation`, not just HEAD. Record `git status --short --branch` and a scoped diff before execution. An isolated worktree created from HEAD will not contain the current uncommitted implementation; carry the agreed baseline deliberately if isolation is chosen.

Existing plans `2026-09-19-google-sdk-neuron-e2e.md`, `2026-09-19-e2e-elon-ui.md`, and `2026-09-20-next-steps.md` contain older testing architecture. This plan supersedes those choices. Do not overwrite their unrelated work or use their old CLI examples without verifying the pinned runner.

All paths below are repository-relative. Proposed code is a design contract; complete namespace/usings from the owning projects. Verify third-party signatures against the pinned packages using Context7 when available, otherwise official documentation/source. An unavailable tool is not a reason to claim a check passed.

Use the repository's .NET SDK and Microsoft.Testing.Platform runner. Baseline command family:

```powershell
dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false
dotnet test --project <test-project.csproj> -p:CodeGraphRefresh=false
```

Before the first targeted run, inspect `dotnet test --help` and that test executable's help to verify supported filters. Do not assume VSTest `--filter` or RunSettings applies. The full project commands below intentionally avoid runner-specific filtering. Capture baseline failures separately.

After each task, inspect the scoped diff and commit only its explicitly named files when executing the plan. Commit labels below are suggested checkpoints, not instructions to stage unrelated work.

## File and dependency map

| Path | Responsibility |
|---|---|
| `src/Testing/DigitalBrain.Testing/` | Shared observations, behavior runs, limits, session lifetime, diagnostics |
| `src/Testing/DigitalBrain.Testing.Unit/` | Unit options, in-process cluster, unit lifecycle operations |
| `src/Testing/DigitalBrain.Testing.Hosting/` | Internal Aspire ownership, identity/storage policy, remote client and configuration transport |
| `src/Testing/DigitalBrain.Testing.Integration/` | Module integration factory and `IntegrationBrain` |
| `src/Testing/DigitalBrain.Testing.E2E/` | Application factory, `E2EBrain`, browser sessions |
| `src/Testing/DigitalBrain.Testing.ModuleRunner/` | One external runtime executable and module asset resolver |
| `src/Testing/DigitalBrain.Testing.ModuleAppHost/` | One framework-owned infrastructure AppHost |
| `src/Testing/DigitalBrain.Testing.Framework.Tests/` | Container-free tests for framework contracts, lifetime, composition |
| `src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/` | External-runner, process lifecycle, isolation, storage tests |
| `src/Modules/DigitalBrain/DigitalBrain/Composition/` | Production module definitions and application snapshot contract |
| `src/Modules/DigitalBrain/Aspire/` | Shared production runtime bootstrap |
| `src/Applications/IntoChat/Configuration/` | Typed application configuration library |
| `src/Modules/Google/Integration/` | Google module HTTP/infrastructure tests |
| `DigitalBrain.Testing.slnx` | Explicit framework integration and application E2E test lane |

Supporting projects do not create more public framework layers. Keep the shared public package free of Aspire, Playwright, Orleans.TestingHost and product-module references. Keep `.Testing.Unit` out of Integration/E2E transitive references.

## Task 1: Prove the shared external runner can load selected modules

**Purpose:** Resolve the riskiest assumption before production refactoring or mass renames. This is a bounded implementation feasibility milestone, not a general plugin loader project.

**Files:**
- Create `src/Testing/DigitalBrain.Testing.ModuleRunner/DigitalBrain.Testing.ModuleRunner.csproj`, `Program.cs`, `ModuleAssetResolver.cs`.
- Create `src/Testing/DigitalBrain.Testing.ModuleAppHost/DigitalBrain.Testing.ModuleAppHost.csproj`, `AppHost.cs`.
- Create `src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/DigitalBrain.Testing.Framework.IntegrationTests.csproj`, `RunnerLoadingFacts.cs`, `Fixtures/RunnerProbe.cs`.
- Create `docs/superpowers/plans/2026-09-20-module-runner-feasibility.md` during implementation to record exact working launch/dependency strategy and measured startup.

**Interfaces:** Consumes existing `IModule`, `IGmail`, `IDigitalBrain`, production `AddDigitalBrain` and module hosting projections. Produces a proven executable/bundle launch strategy for Task 5; no final public API commitment yet.

- [ ] Add `RunnerProbe.StartAsync(CancellationToken)` in the test fixture: own a run directory, start the shared AppHost with selected Google assembly paths, launch the runner, connect a production brain client, expose `Brain`, `HttpClient`, `ProcessId`, and bounded `DisposeAsync`. During the probe, setup may be explicit fixture code; no product-module reference is allowed in the runner project.
- [ ] Add the behavioral acceptance test below and run the new integration project to establish the missing-loader failure.

```csharp
[Fact]
public async Task GoogleLoadsWithoutAStaticRunnerReference()
{
    var ct = TestContext.Current.CancellationToken;
    await using var run = await RunnerProbe.StartAsync(ct);
    Assert.NotEqual(Environment.ProcessId, run.ProcessId);
    var gmail = run.Brain.Get<IGmail>("runner-probe");
    await using var received = await run.Brain.Observe<MailReceived>(gmail, ct);
    var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(
        "{\"emailAddress\":\"runner-probe\",\"historyId\":\"123\"}"));
    using var response = await run.HttpClient.PostAsJsonAsync(
        "/google/gmail/watch", new { message = new { data } }, ct);
    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.Equal("123", (await received.NextAsync(ct: ct)).HistoryId);
}
```

- [ ] Resolve selected runtime assemblies before Orleans discovers grain implementations/serializers. Use one shared identity for contract/Orleans assemblies. Explicitly test host-side adapter loading in the separate AppHost dependency context.
- [ ] Add `MissingModuleAssemblyFailsBeforeHealthWait`, `ConflictingSharedVersionIsRejected`, `TransitiveManagedDependencyLoads`, and `NativeRuntimeAssetResolves` tests. For conflict, deliberately supply a manifest entry requiring an incompatible shared version; for native resolution, package a small RID-appropriate native fixture rather than assuming a managed dependency proves it.
- [ ] Add `CanceledProbeLeavesNoOwnedProcess`: cancel after process creation, await rollback, check the recorded PID has exited and owned resource inventory is empty.
- [ ] Run `dotnet test --project src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/DigitalBrain.Testing.Framework.IntegrationTests.csproj -p:CodeGraphRefresh=false`.
- [ ] Record exact resolver/runtimeconfig/dependency strategy, platform results and limitations. Stop this plan if the gate fails; revise the design instead of switching to in-process execution without saying so.

**Checkpoint:** `Prove external module runner loading and cleanup`.

## Task 2: Separate common test helpers and rename the unit layer

**Files:**
- Move observation files from `src/Testing/DigitalBrain.NeuronTesting/Observation/` into `src/Testing/DigitalBrain.Testing/Observation/`.
- Create `src/Testing/DigitalBrain.Testing/DigitalBrain.Testing.csproj`, `BrainTestExtensions.cs`, `Lifetime/ITrackedBrain.cs`.
- Move remaining unit project to `src/Testing/DigitalBrain.Testing.Unit/`; rename its project file to `DigitalBrain.Testing.Unit.csproj` and `SimulationOptions.cs` to `UnitOptions.cs`.
- Modify `DigitalBrain.Foundation.slnx`, `DigitalBrain.slnx`, `src/Testing/Module.Tests.props`, and current test project references found by `rg -n 'DigitalBrain.NeuronTesting|DigitalBrain.E2ETesting'`.
- Create `src/Testing/TestRunner.props`, `src/Testing/Unit.Tests.props`.

**Interfaces:** Shared `Observe<T>` and `RunBehavior` retain their call shape. Unit produces `Task<UnitBrain> StartAsync(UnitOptions, CancellationToken)`; initially accept current module instances until Task 4 replaces the registration input.

- [ ] Preserve existing runtime harness tests as regression coverage. Build the new common package without Aspire/TestHost/Playwright/xUnit references.
- [ ] Move only general observation code; separate unit cluster/restart/deactivation extensions onto `UnitBrain` rather than casting arbitrary brains.

```csharp
// Shared public helpers remain available for production IDigitalBrain values.
public static Task<SignalProbe<T>> Observe<T>(
    this IDigitalBrain brain, INeuron source,
    CancellationToken cancellationToken = default) where T : Signal;

// Unit-only operations are visible only on the unit handle.
public static Task RestartSiloAsync(
    this UnitBrain brain, CancellationToken cancellationToken = default);
```

- [ ] Move xUnit/MTP compiler defaults to `TestRunner.props`; select layer dependencies explicitly in test projects or layer-specific props. Temporarily keep `Module.Tests.props` as a forwarding import for existing unit consumers only; remove that import from integration consumers.
- [ ] Run `dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false`. Confirm discovery counts do not drop and no Docker/browser process is required.
- [ ] Inspect project references to ensure this task has not introduced a production-to-testing edge.

**Checkpoint:** `Extract shared testing helpers and name the unit layer`.

## Task 3: Make session ownership and failure diagnostics consistent

**Files:**
- Create `src/Testing/DigitalBrain.Testing/Lifetime/TestSessionLifetime.cs`, `Diagnostics/TestDiagnostic.cs`, `Diagnostics/ITestDiagnosticSink.cs`, `TestExecutionOptions.cs`.
- Modify unit `Host/UnitBrain.cs` (renamed from `SimulatedBrain.cs`).
- Create `src/Testing/DigitalBrain.Testing.Framework.Tests/LifetimeFacts.cs`, `TestExecutionOptionsFacts.cs`, and project file importing `TestRunner.props`.

**Interfaces:** `TestSessionLifetime.Own(string stage, IAsyncDisposable resource)`, `ValueTask DisposeAsync()`, and diagnostic sink `void Write(TestDiagnostic diagnostic)`. `TestExecutionOptions` contains startup/assertion/cleanup deadlines and optional sink; defaults match spec.

- [ ] Add tests proving cleanup attempts later resources after an earlier exception, repeated disposal is harmless, and partially acquired sessions roll back.

```csharp
[Fact]
public async Task CleanupAttemptsEveryResource()
{
    var disposed = new List<string>();
    var lifetime = new TestSessionLifetime(new TestExecutionOptions());
    lifetime.Own("first", new RecordingResource(disposed, "first", fail: false));
    lifetime.Own("second", new RecordingResource(disposed, "second", fail: true));
    await Assert.ThrowsAsync<AggregateException>(
        () => lifetime.DisposeAsync().AsTask());
    Assert.Contains("first", disposed);
    Assert.Contains("second", disposed);
    await lifetime.DisposeAsync();
    Assert.Equal(2, disposed.Count);
}
```

`RecordingResource` is a test-local `IAsyncDisposable` that appends its label, then optionally throws `IOException`. It exists only in `LifetimeFacts.cs`.

- [ ] Implement immediate ownership registration, deterministic stage ordering, one cleanup deadline and structured aggregate failures. Reject new ownership after disposal begins and dispose/reject the incoming resource without leaking it.
- [ ] Test concurrent ownership/disposal, never-completing cleanup, cancellation callback failures, and already-canceled test tokens. Unit uncooperative tasks are reported, not claimed to be terminated.
- [ ] Preserve failure causes in startup rollback. Document plain `await using` body/disposal exception masking; do not add xUnit integration to hide it.
- [ ] Run framework unit tests and the foundation solution once after the changes.

**Checkpoint:** `Centralize bounded test resource ownership`.

## Task 4: Introduce module-owned typed composition and configuration snapshots

**Files:**
- Create production `Composition/ModuleDefinition.cs`, `ModuleComposition.cs`, `ApplicationConfigurationSnapshot.cs`, `IApplicationConfiguration.cs`, `SecretReference.cs` under `src/Modules/DigitalBrain/DigitalBrain/`.
- Create Google `Configuration/GoogleModuleOptions.cs`; add `GoogleModule.Define` in `GoogleModule.cs`.
- Create Flutter `Configuration/FlutterModuleOptions.cs`; add `FlutterModule.Define` in `FlutterModule.cs`.
- Add equivalent small definitions for `TimeModule` and `TestTwitterModule` without inventing unused configuration knobs.
- Modify production hosting/runtime module configuration consumers and unit `UnitOptions`/factory.
- Create framework unit `CompositionFacts.cs`, `ConfigurationSnapshotFacts.cs`.

**Interfaces:** Module-owned `Define(TypedOptions)` returns immutable `ModuleDefinition`. `ModuleComposition.Resolve(IReadOnlyList<ModuleDefinition>)` returns validated ordered definitions. `IApplicationConfiguration.CreateSnapshot()` returns `ApplicationConfigurationSnapshot`. Definition serialization includes stable schema IDs and public settings, not credentials or callbacks.

- [ ] Write tests for equal-definition deduplication, conflicting definitions, dependency cycles, immutable snapshots, and missing module descriptors.

```csharp
[Fact]
public void ConflictingModuleSettingsAreRejected()
{
    var first = FlutterModule.Define(new()
    {
        Hosting = new() { Kind = FlutterHostKind.Web }
    });
    var second = FlutterModule.Define(new()
    {
        Hosting = new() { Kind = FlutterHostKind.None }
    });
    Assert.Throws<InvalidOperationException>(
        () => ModuleComposition.Resolve([first, second]));
}
```

- [ ] Implement production composition contracts without referencing Aspire or tests. Keep hosting mappings in the module's existing Aspire.Hosting adapter. Resolve any runtime/hosting options assembly cycle by moving plain shared configuration records into a module-owned lightweight configuration contract location; do not make the runtime reference Aspire.Hosting.
- [ ] Map all module-owned keys explicitly. Add tests preserving `false`, empty lists, and valid empty strings; reject invalid required values with property paths. Keep public snapshot and secret resolution separate.
- [ ] Add a known secret marker through the credential resolver and assert it is absent from public serialization, exception messages, `ToString()` and diagnostic records. Do not serialize `GmailOAuthOptions`.
- [ ] Change unit examples/tests to `GoogleModule.Define(new())`. Preserve unit-only `ConfigureSilo` callbacks as local overrides. Unit must not start any hosting projection.
- [ ] Run framework unit tests, Google unit tests, and the foundation solution.

**Checkpoint:** `Share typed module composition across production and tests`.

## Task 5: Turn the runner proof into deterministic build assets

**Files:**
- Create `src/Testing/DigitalBrain.Testing.Integration/buildTransitive/DigitalBrain.Testing.Integration.targets` and project file.
- Add runner `ModuleLaunchManifest.cs`, `ModuleBundleValidator.cs`; evolve Task 1's `ModuleAssetResolver.cs`.
- Modify shared ModuleAppHost loading and test project build integration.
- Create framework integration `BundleFacts.cs`.

**Interfaces:** Build emits versioned `module-bundle.json` and a runnable bundle under the test output. `ModuleBundleValidator.Validate(path, selectedDefinitions)` returns validated launch metadata or a detailed failure. Bundle selection is by module identity; source paths are not hard-coded.

- [ ] Replace probe assembly paths with build-generated assets using the exact successful resolver strategy from Task 1. Include host-side adapters and runtime-side assets in their respective dependency contexts.
- [ ] Test a clean build followed by `--no-build` execution, copying the output to a different directory, and paths containing spaces/non-ASCII characters.
- [ ] Test stale manifest schema, omitted module assembly, incompatible shared version and duplicate asset names. Failure must identify the offending module/asset before starting containers.
- [ ] Launch with an argument list, never interpolated shell commands. Public manifests contain secret references only. The same rules apply on Windows and Linux.
- [ ] Prove the runner and ModuleAppHost project files contain no static Google/IntoChat reference; the integration test build supplies selected module assets.
- [ ] Run the external-runner acceptance project from a clean output. Record the resulting manifest schema and dependency rules in the runner README.

**Checkpoint:** `Build portable selected-module runner bundles`.

## Task 6: Implement shared Aspire ownership, isolation and connections

**Files:**
- Create `src/Testing/DigitalBrain.Testing.Hosting/DigitalBrain.Testing.Hosting.csproj`, `AspireTestSession.cs`, `IsolatedExecutionPolicy.cs`, `BrainConnection.cs`, `ConfigurationTransport.cs`.
- Modify `src/Modules/DigitalBrain/Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs` to expose an explicit storage-lifetime seam without changing development defaults.
- Create framework integration `StartupFailureFacts.cs`, `IsolationFacts.cs`.

**Interfaces:** `AspireTestSession` owns the distributed application/client and resolves primary brain/HTTP metadata. `IsolatedExecutionPolicy` owns generated identity, ephemeral storage, resource names and endpoints. `BrainConnection` configures the remote production client from resolved connection/identity metadata.

- [ ] Add tests for two simultaneous runs using the same grain key with different values; each must see only its own state. Use a durable test neuron in `Fixtures/PersistentValueModule.cs` with `Task Set(int)` and `Task<int> Get()` methods and production grain storage.
- [ ] Add failure injection after graph build, process start and client connection. Assert recorded process exits and owned infrastructure cleanup after each failure; exercise cleanup failure independently from the original startup error.
- [ ] Implement one startup cancellation deadline shared across phases. Replace per-operation timeout resets. Configure cluster/service identity once and project it to both host and client.
- [ ] Disable persistent container/volume reuse only for test-owned resources. Validate conflicting application overrides of harness-owned identity/connection settings before graph start.
- [ ] Capture bounded resource logs and exit state. Test that early process failure wakes startup immediately instead of waiting the whole deadline.
- [ ] Test cancellation before startup, during health wait and during client connection; use a fresh cleanup deadline and kill only owned process trees after graceful shutdown expires.
- [ ] Run framework integration tests. Inspect the post-test resource inventory for run-owned leftovers.

**Checkpoint:** `Own isolated Aspire test sessions and remote brain clients`.

## Task 7: Deliver module integration and migrate Google HTTP tests

**Files:**
- Create integration `IntegrationOptions.cs`, `ModuleDigitalBrainSimulation.cs`, `IntegrationBrain.cs`.
- Extract reusable runtime setup under `src/Modules/DigitalBrain/Aspire/BrainRuntimeHost.cs`; call it from runner and preserve existing IntoChat-specific middleware.
- Move `src/Modules/Google/E2E/` to `src/Modules/Google/Integration/`, rename project to `DigitalBrain.Modules.Google.Integration.csproj`.
- Move `src/Modules/Google/Tests/Gmail/GmailOAuthCallbackFacts.cs` into Google Integration.
- Remove Google unit project's E2ETesting reference; replace Google integration's IntoChat reference with the shared runner/build asset integration.
- Create framework integration `RuntimeRestartFacts.cs`.

**Interfaces:** `IntegrationOptions.Modules` is `IReadOnlyList<ModuleDefinition>` and `Execution` is `TestExecutionOptions` (not an execution-mode switch). Factory returns `IntegrationBrain : IDigitalBrain` with `HttpClient` and `RestartRuntimeAsync(ct)`.

- [ ] Migrate Gmail webhook test to the canonical spec example. Keep test payload construction and expected HTTP status visible; remove test-owned Kestrel plumbing.
- [ ] Prove real storage survival using the persistent value test neuron:

```csharp
await using var brain = await ModuleDigitalBrainSimulation.StartAsync(options, ct);
await brain.Get<IPersistentValue>("same-key").Set(42);
await brain.RestartRuntimeAsync(ct);
Assert.Equal(42, await brain.Get<IPersistentValue>("same-key").Get());
```

`options` selects the `PersistentValueModule` fixture from Task 6. Its contract and generated serializers are bundled just like a real module.

- [ ] Verify restart invalidates old observations, reconnects the public brain handle, and retains storage while other runs remain isolated. Add a real Time reminder restart/reactivation scenario using existing Time contracts and their documented minimum-period controls.
- [ ] Replace the OAuth callback test's in-process fake with a controlled HTTP provider stub. First inspect the current SDK token-exchange path: configure its actual external transport seam or add a module-owned endpoint option plus a production adapter test. Do not replace the callback under test or send a fake service instance to the runner.
- [ ] Confirm only selected modules register endpoints/resources. Google integration must not start Flutter or IntoChat.
- [ ] Run Google unit and integration projects independently; run runtime restart tests. Remove `ModuleWebHost` only after `rg` shows no callers.

**Checkpoint:** `Run module integration through the shared external runtime`.

## Task 8: Make application behavior readiness explicit

**Files:**
- Modify `src/Modules/DigitalBrain/BehaviorRuntime/BehaviorHosting.cs`.
- Create sibling `BehaviorReadiness.cs`, `BehaviorReadinessHealthCheck.cs`, `BehaviorScopedBrain.cs`.
- Modify `src/Applications/IntoChat/IntoChat/Behavior/BehaviorEndpoints.cs`, `src/Behaviors/Fakes/TwitterFakes.cs`.
- Create `src/Behaviors/Tests/BehaviorReadinessFacts.cs`; extend `ElonBitcoinFacts.cs`.

**Interfaces:** Application registration declares required subscriptions for a behavior; `BehaviorReadiness` tracks `(behavior, generation, source, signalType)` plus failures. Production host creates a behavior-scoped brain decorator; successful subscription acquisition updates the current generation, disposal/completion invalidates it.

- [ ] Write tests where one of two required subscriptions is missing, an old generation reports ready after restart, and a required behavior faults while retrying. None may report ready.

```csharp
var status = new BehaviorReadiness();
var first = status.Begin("elon", requirements);
var second = status.Begin("elon", requirements);
status.SubscriptionReady(first, source, signalType);
Assert.False(status.IsReady("elon"));
status.SubscriptionReady(second, source, signalType);
Assert.True(status.IsReady("elon"));
```

`requirements` is an immutable list of `SubscriptionRequirement` values; define this record in `BehaviorReadiness.cs` with source identity and signal type. `Begin` returns a `BehaviorGeneration` token. The test above uses one requirement; multi-requirement coverage is a separate test.

- [ ] Decorate the brain supplied to hosted behaviors, not the global test client. Intercept completed `SubscribeAsync` calls and subscription termination. Record failures before the existing retry delay; readiness stays false until a new generation meets every requirement.
- [ ] Register IntoChat's Elon subscription as required; expose aggregate readiness via the application's normal health checks. Do not require the input event itself for readiness.
- [ ] Remove `_last` replay from `TwitterAccount.Watch`. Add a test that a late observer receives no historical tweet and a ready observer receives a newly published tweet. Use bounded negative observation rather than arbitrary long sleeps.
- [ ] Run behavior unit tests and an application test that posts immediately after harness startup, with no delay/retry. Assert exact output once.

**Checkpoint:** `Wait for hosted behavior subscriptions without replay`.

## Task 9: Typed IntoChat options and actual-AppHost E2E

**Files:**
- Create `src/Applications/IntoChat/Configuration/IntoChat.Configuration.csproj`, `IntoChatOptions.cs`, `IntoChatComposition.cs`.
- Modify `src/Applications/IntoChat/AppHost/AppHost.cs`, its project references and application runtime option binding.
- Rename `src/Testing/DigitalBrain.E2ETesting/` to `src/Testing/DigitalBrain.Testing.E2E/`; replace old factory/options/wrapper with `E2EDigitalBrainSimulation.cs`, `E2EBrain.cs`.
- Modify `src/Applications/IntoChat/Tests/IntoChat.Tests.csproj`, `HealthFacts.cs`, `GmailWatchFacts.cs`, `ElonInboxHttpFacts.cs`.
- Create application `ConfigurationFacts.cs`; create framework unit `EndpointSelectionFacts.cs`.

**Interfaces:** `IntoChatOptions : IApplicationConfiguration` composes module-owned typed settings, including the current demo module. Generic factory consumes the production configuration snapshot and returns `E2EBrain`; its overload accepting `TestExecutionOptions` keeps test deadlines/diagnostics separate from application settings. AppHost marks its primary brain, HTTP endpoint and optional browser endpoint using production hosting metadata.

- [ ] Add tests that Web versus None changes only intended Flutter hosting, typed defaults match normal AppHost defaults, false/empty values remain explicit, and mutation after startup cannot change a run.
- [ ] Map options before AppHost composition and project runtime settings to child processes. Do not build the full graph then remove resources to imitate a module integration test.
- [ ] Remove IntoChat/Flutter names and raw cluster IDs from generic test defaults. Fail on missing or ambiguous primary endpoint metadata; test both cases without containers.
- [ ] Implement E2E startup over the shared Aspire session with actual AppHost health/readiness. Preserve application authentication/CORS and resolve origins from the real selected endpoints.
- [ ] Update HTTP tests to `brain.HttpClient` and typed `IntoChatOptions`. Use bounded outcome waiting, never resubmit the webhook while polling the outcome. Strengthen Elon inbox assertion to include the configured Bitcoin value.
- [ ] Run IntoChat HTTP E2E with Flutter disabled explicitly in typed options. Verify no generic framework project references IntoChat.

**Checkpoint:** `Start real application E2E from typed options`.

## Task 10: Browser ownership, endpoint discovery and UI failure artifacts

**Files:**
- Create E2E `Browser/BrowserSession.cs`, `Browser/BrowserLifetime.cs`.
- Modify `src/Applications/IntoChat/Tests/ElonBitcoinUiFacts.cs` and remove direct browser process plumbing.
- Create framework integration `BrowserLifetimeFacts.cs`.

**Interfaces:** `Task<BrowserSession> E2EBrain.OpenBrowserAsync(CancellationToken)`; overload `OpenBrowserAsync(BrowserOptions, CancellationToken)` accepts E2E-owned browser preferences. `BrowserSession.Page` is native Playwright `IPage`; session disposal closes its isolated context, brain disposal closes the engine. Common execution options specify bounded artifact retention; E2E-owned `BrowserOptions` specifies launch preferences, with headless as the test default.

- [ ] Write a test with a dynamically assigned UI endpoint to prove navigation does not rely on `54723` or a guessed address.
- [ ] Implement browser startup lazily, track ownership immediately, create a context per session, and navigate to the declared endpoint. Throw a descriptive error if the selected application configuration exposes no browser endpoint.
- [ ] Use native Playwright assertions in the migrated Elon test. Set explicit finite waits; close the context on cancellation and preserve cancellation as the failure cause where possible.
- [ ] Add tests for cancellation during navigation, browser launch failure, repeated disposal, and artifact writer failure. Assert process/context cleanup still completes. Artifacts use run-specific paths and bounded retention, never secrets in filenames.
- [ ] Run the actual Flutter Web E2E once with required browser/toolchain dependencies installed. Missing dependencies are an explicit failed prerequisite, not a silent skip counted as success.

**Checkpoint:** `Own E2E browser sessions and discover UI endpoints`.

## Task 11: Finish migration, validate dependency direction and document usage

**Files:**
- Modify `src/Testing/README.md`, `DigitalBrain.slnx`, `DigitalBrain.Foundation.slnx`.
- Create `DigitalBrain.Testing.slnx` and framework `README.md` for bundle/runtime prerequisites.
- Update remaining repository project references/imports/usings returned by scoped searches.
- Remove obsolete `E2EDigitalBrain`, `brain.Http()` wrapper, `ModuleWebHost`, and old package directories after migration.

**Interfaces:** Final public names are those in the spec. No compatibility aliases are required for this repository-internal redesign unless an actual external consumer is discovered and documented before removal.

- [ ] Search for old package/factory/project names and distinguish historical docs from executable references. Fix live references; annotate superseded docs without rewriting unrelated history.
- [ ] Add dependency architecture checks using project-reference graphs: shared core has no forbidden framework references; Unit has no Aspire; Integration/E2E have no Unit reference; Google integration has no IntoChat; production has no test reference.
- [ ] Add a small compilation/usage test for each public factory so canonical README examples cannot drift. Do not add tests that merely restate individual forwarding methods.
- [ ] Document typed option defaults, no replay/retry semantics, local Azure emulator scope, secret handling, isolation, supported bundle platforms, startup/cleanup deadlines and prerequisites.
- [ ] Record initial xUnit v3 4.0 scheduling explicitly. Unit can run broadly in parallel; expensive suites use a bounded setting verified with the pinned MTP runner. Do not rely on fixture sharing or test-class inheritance for isolation.
- [ ] Run the acceptance matrix below and record actual outcomes, durations and remaining limitations. Expand testing only for new changes, failures or unresolved risks.
- [ ] Review the whole diff against the spec and current user changes; commit only implementation-owned files.

**Checkpoint:** `Complete testing framework migration and acceptance documentation`.

## Acceptance matrix

| Check | Command / evidence | Required outcome |
|---|---|---|
| Fast foundation | `dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false` | No container/browser requirement; existing neuron semantics preserved |
| Shared contracts/lifetime | `dotnet test --project src/Testing/DigitalBrain.Testing.Framework.Tests/DigitalBrain.Testing.Framework.Tests.csproj -p:CodeGraphRefresh=false` | Ownership, composition, diagnostics and options tests pass |
| Google unit | `dotnet test --project src/Modules/Google/Tests/DigitalBrain.Modules.Google.Tests.csproj -p:CodeGraphRefresh=false` | No HTTP host or E2E dependency |
| Runner/infrastructure | `dotnet test --project src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/DigitalBrain.Testing.Framework.IntegrationTests.csproj -p:CodeGraphRefresh=false` | Loading, cancellation, isolation, restart and cleanup pass |
| Google integration | `dotnet test --project src/Modules/Google/Integration/DigitalBrain.Modules.Google.Integration.csproj -p:CodeGraphRefresh=false` | Real webhook/OAuth transport without IntoChat or Flutter |
| Application E2E | `dotnet test --project src/Applications/IntoChat/Tests/IntoChat.Tests.csproj -p:CodeGraphRefresh=false` | Real AppHost HTTP and Flutter UI flows pass |
| Platform portability | Run bundle tests on Windows and Linux with their native runtime assets | No dependency on author machine paths or shell quoting |
| Resource audit | Inspect run-owned process/resource inventory after normal and failed suites | No leaked process/container/context; no user-owned resources stopped |
| Dependency graph | Project graph checks plus search for obsolete active references | Exactly the intended public dependency direction |

Do not claim multi-platform support from a single-platform run. If Flutter or Docker is unavailable, record the blocked acceptance row and complete other independent checks; the framework is not fully accepted until required rows pass.

## Sequence and checkpoints

Task 1 is a hard feasibility gate. Tasks 2–4 establish stable contracts and ownership. Tasks 5–7 deliver independently usable module integration. Task 8 fixes production readiness before Tasks 9–10 depend on it. Task 11 removes transitional paths and validates the whole design.

A useful first implementation milestone is Tasks 1–7: unit and Google module integration work independently of IntoChat. The second is Tasks 8–10: actual application readiness and E2E. This ordering keeps the external runner risk from spreading into application changes.

No implementation has been performed as part of writing this plan. Before execution, review the spec and plan together. Native execution is a reasonable default because tasks share composition and lifecycle contracts; task-by-task delegated execution is also possible if explicitly selected.
