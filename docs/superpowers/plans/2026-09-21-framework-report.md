# Task 1 framework report

Implemented in the existing `archv2` checkout, without commits or pushes. The prior Dart console host removal remains; VM/DDS hot reload implementation is unchanged.

## API and ownership

- `E2ETest.Create().WithModule<FlutterModule>().StartAsync(ct)` reuses `BrainCompositionBuilder` and the existing module bundle, ModuleAppHost, and ModuleRunner through shared `ModuleTestHost` startup.
- Optional core `IModuleE2EDefaults<TModule>` lets the selected module supply E2E defaults. Flutter selects web hosting. Defaults run before explicit caller configuration, so `BackendOnly()` wins. Integration and application builders do not invoke these defaults.
- `E2ETest.For<AppHost>()` transports explicit overrides only and preserves the actual application graph.
- Both startup paths automatically open a headless browser when the host declares a browser endpoint. `brain.Page` is ready on return. Additional independent sessions remain available through `OpenBrowserAsync`.
- Hosting APIs are now `BackendOnly`, `RunWebApp`, and `RunDesktopApp`.
- New `src/Modules/Flutter/Testing/DigitalBrain.Modules.Flutter.Testing.csproj` contains the testing-only `RunWebApp(b => b.Headed().SlowMo(250))` overload in `DigitalBrain.Flutter`. Browser callbacks are captured in a scoped process-local E2E configuration context, never serialized into production module options. Builders retain immutable option snapshots and default explicitly to headless.
- Startup failure/cancellation disposes the acquired E2E owner (including host, browser and contexts), preserving the original error if cleanup also fails. Existing late browser acquisition and context cancellation tests remain.
- ModuleRunner allows loopback browser CORS; Flutter package lookup now supports the ModuleAppHost directory under `src/Testing`, as well as application AppHosts.

## Verification

- Initial preimplementation framework build was blocked by concurrently moved ConfigurationFacts missing a Google reference, so this was not a valid red behavioral result for the new builder API. The parent supplied the reference.
- New `ModuleAppHostFindsFlutterWithoutAnApplicationDirectoryOverride` regression test was run before the package lookup fix. It failed with the wrong `E:\intochat\digitalbrain\Modules\Flutter\app\core` path; the fixed resolver finds the real package.
- Final command: `dotnet run --project src/Testing/DigitalBrain.Testing.Framework.Tests --no-restore -- --progress off`.
- Result: **65 passed, 0 failed, 0 skipped**, including real Playwright lifecycle tests. This builds E2E, Integration, ModuleAppHost, ModuleRunner, Flutter hosting, and the new Flutter testing adapter.
- Added focused tests for selected module defaults versus explicit BackendOnly, application override preservation/rejection of undeclared modules, local browser settings/builder isolation, and failure/cancellation host cleanup.
- Preserved cross-module Excel/Flutter serialized sheet identity coverage in `ModuleIdentityFacts`.
- Extended existing Framework.IntegrationTests simultaneous deployment test with distinct HTTP port assertion; its real runtime execution is delegated to coordinated final integration verification, not claimed passed here.
- Context7 lookup for Aspire directory configuration was attempted; service returned monthly quota exceeded. The implementation instead uses existing local hosting interfaces and a direct package resolver regression test.

## Rulings

- Use a small module-specific testing adapter instead of referencing Flutter from generic E2E infrastructure. Cost: browser callback callers add a Flutter.Testing project reference; production stays free of browser options.
- Keep `OpenBrowserAsync` for genuinely additional sessions while making ordinary authoring use automatic `Page`.

No application test files, solution files, or CI files were edited by this worker. Parent handles adapter solution/reference inclusion and application API migration. Module worker handles physical module test projects and real UI coverage.

## Follow-up integration verification

- `dotnet run --project src/Testing/DigitalBrain.Testing.Framework.IntegrationTests -- --progress off` completed successfully: **7 passed, 0 failed, 0 skipped**, 46.152 seconds of test execution. This includes the changed simultaneous-runtime distinct-port assertion, external provider loading, restart durability, and cross-run state isolation. Output: `.framework-integration-test.log` (local verification log).
- Installed `.NET 11 dotnet test --help` confirms `--max-parallel-test-modules <NUMBER>` controls simultaneous test assemblies. Recommended CI invocation adds `--max-parallel-test-modules 1`; preserve each E2E assembly's xUnit collection serialization as well. `--parallel none` alone does not serialize separate assemblies.
- Parent framework review reported no findings.

## Whitespace verification

- Trimmed excess trailing blank lines in owned framework/Flutter C# and project files, then applied `dotnet format whitespace DigitalBrain.slnx --no-restore --include <owned changed/new C# files>`.
- The same include scope with `--verify-no-changes` passed. Repository-wide `git diff --check` passed.
- No behavior changes or builds in this formatting pass; active module E2E execution was left undisturbed.
