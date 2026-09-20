# DigitalBrain testing framework design

Date: 2026-09-20. Status: design and implementation-plan deliverables for review; implementation has not started.

## 1. Intent and decisions

Provide one testing vocabulary across three execution layers. A test selects its composition or application, supplies typed configuration, receives a brain, and describes a behavior. Infrastructure configuration, process ownership, readiness, isolation, and diagnostics belong to the framework.

Agreed decisions:

- Public packages are `DigitalBrain.Testing`, `DigitalBrain.Testing.Unit`, `DigitalBrain.Testing.Integration`, and `DigitalBrain.Testing.E2E`.
- Use explicit asynchronous startup and `await using` in tests. No test base classes, custom xUnit runner, or automatic fixture sharing in the initial implementation.
- Unit tests use the lightweight in-process Orleans test cluster.
- Integration tests select modules and use one shared external module runner with Aspire-managed infrastructure. No AppHost project per module and no dependency on IntoChat.
- E2E tests start the application's actual AppHost with application-owned typed options. No named IntoChat test profiles.
- Every brain implements production `IDigitalBrain`. Integration and E2E return richer concrete types that expose HTTP; E2E also supports an optional test browser.
- Production module registration and hosting code are shared. Tests do not recreate module endpoints, storage providers, or application behaviors.
- External runner packaging is an implementation feasibility gate, not an already-proven capability.

Non-goals: live Google/Azure account tests by default; automatic retries of actions under test; signal replay; a generic dependency-injection override protocol across processes; hot reconfiguration; test-time compilation; fixture pooling; universal module migration; new product browser neurons.

## 2. Evidence from the current working tree

The branch is `codex/framework-foundation`, six commits ahead of origin when inspected. The working tree already contains user changes to the testing framework, Google tests, IntoChat, and generated Flutter files. Preserve those changes. Earlier plans dated 2026-09-19 and the existing 2026-09-20 next-steps plan describe older hosting choices; this document supersedes their testing architecture, not their unrelated product work.

- `src/Testing/DigitalBrain.NeuronTesting` owns simulation plus generally useful `Observe`, `SignalProbe`, `BehaviorRun`, and bounded waits.
- `src/Testing/DigitalBrain.E2ETesting` currently references NeuronTesting, embeds IntoChat resource names in `E2EOptions`, and exposes HTTP through a runtime cast on `IDigitalBrain`.
- Google `E2E/GmailWatchWebhookFacts.cs` now starts IntoChat's AppHost; Google unit tests also reference E2ETesting to run `ModuleWebHost` for OAuth.
- `Module.Tests.props` implicitly imports NeuronTesting, even for module E2E projects.
- IntoChat's UI test hard-codes the Flutter address and owns browser startup.
- `AddDigitalBrain` in production Aspire hosting creates persistent Azurite storage with a data volume. This is unsuitable as the default test isolation policy.
- Runtime module loading uses assembly-qualified names and `Activator.CreateInstance`; the external runner must make the correct assemblies and Orleans-generated metadata available before runtime startup.
- `BehaviorHost<T>` retries exceptions without reporting a readiness state. `TwitterAccount.Watch` currently replays the last tweet, which can hide subscription races and conflicts with the live-only signal contract.
- Current `GmailOAuthOptions` explicitly forbids serializing/logging its credential-bearing instance. Configuration transport must honor this.

## 3. Layer guarantees

| Layer | Composition owner | Runtime | Providers | Main proof |
|---|---|---|---|---|
| Unit | Test | In-process Orleans test cluster | Memory storage and controlled dependencies | Neuron and behavior contracts |
| Integration | Test selects module definitions | Shared runner process, production HTTP/Orleans host | Aspire-managed isolated infrastructure | Module transport, serialization, persistence and hosting |
| E2E | Actual application AppHost | Actual application processes | Aspire graph under an isolated test execution policy | Application wiring and full scenarios |

Unit does not mean every test is a pure function test. It names the lightweight neuron/behavior layer. Integration may compose more than one module when the interaction is the subject of the test. E2E may inspect neurons; it must stimulate the path it claims to cover, such as HTTP or the UI.

An HTTP acceptance assertion alone does not prove downstream behavior or persistence. Observe the signal, read the state, or assert the visible application outcome. A persistence assertion must survive runtime restart against the same test-owned storage; observing a signal alone does not establish durability.

## 4. Public interface

Proposed contracts below are implementation targets, not currently compiled interfaces.

```csharp
// DigitalBrain.Testing.Unit
Task<UnitBrain> DigitalBrainSimulation.StartAsync(
    UnitOptions options, CancellationToken cancellationToken = default);

// DigitalBrain.Testing.Integration
Task<IntegrationBrain> ModuleDigitalBrainSimulation.StartAsync(
    IntegrationOptions options, CancellationToken cancellationToken = default);

// DigitalBrain.Testing.E2E
Task<E2EBrain> E2EDigitalBrainSimulation.StartAsync<TAppHost>(
    IApplicationConfiguration options,
    CancellationToken cancellationToken = default) where TAppHost : class;

// Explicit execution overrides do not become application configuration.
Task<E2EBrain> E2EDigitalBrainSimulation.StartAsync<TAppHost>(
    IApplicationConfiguration options, TestExecutionOptions execution,
    CancellationToken cancellationToken = default) where TAppHost : class;
```

All three concrete types implement `IDigitalBrain` and `IAsyncDisposable`. `Get<T>` and `SubscribeAsync<T>` delegate to the real client for that run. Shared `Observe<T>` and `RunBehavior` helpers remain extensions on `IDigitalBrain`. `RunBehavior` explicitly runs a test-owned behavior; E2E examples never use it to duplicate an application-hosted behavior.

`UnitBrain` exposes unit-only lifecycle controls through extensions accepting `UnitBrain`: `DeactivateAsync`, `RestartSiloAsync`, and explicit raw-client access for low-level runtime tests. These operations must not appear as extensions on arbitrary `IDigitalBrain` values.

`IntegrationBrain` exposes `HttpClient` and `RestartRuntimeAsync(CancellationToken)`. Restart preserves the run's storage and identity, reconnects its client, and invalidates old observations. Tests acquire new neuron references/subscriptions after restart. It does not restart unrelated infrastructure.

`E2EBrain` exposes `HttpClient` and `OpenBrowserAsync(CancellationToken)`. Browser sessions are test drivers, not neurons. A `BrowserSession` owns an isolated browser context and exposes the underlying Playwright `IPage` as `Page`; use native Playwright assertions rather than inventing an assertion language. The brain owns the browser engine/process. No browser launches unless requested.

Optional browser preferences use an overload `OpenBrowserAsync(BrowserOptions, CancellationToken)` owned by `.Testing.E2E`, with a headless default. Browser-specific settings do not enter the common package or application options.

The default HTTP endpoint and optional browser endpoint are explicit application-host metadata. Multiple candidate brains/endpoints without an explicit primary selection fail startup; never guess by resource order or application name. The initial public API supports one primary brain and HTTP surface per run.

### Unit example

```csharp
var ct = TestContext.Current.CancellationToken;
await using var brain = await DigitalBrainSimulation.StartAsync(
    new UnitOptions { Modules = [GoogleModule.Define(new())] }, ct);

var gmail = brain.Get<IGmail>("user@gmail.com");
await using var received = await brain.Observe<MailReceived>(gmail, ct);
await gmail.AcceptWatchPush(new GmailWatchPush("123", "user@gmail.com"));
Assert.Equal("123", (await received.NextAsync(ct: ct)).HistoryId);
```

`GmailWatchPush` is the existing Google contract, with history ID first and email address second.

### Integration example

```csharp
var ct = TestContext.Current.CancellationToken;
await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
    new IntegrationOptions { Modules = [GoogleModule.Define(new())] }, ct);

var gmail = brain.Get<IGmail>("user@gmail.com");
await using var received = await brain.Observe<MailReceived>(gmail, ct);
using var response = await brain.HttpClient.PostAsJsonAsync(
    "/google/gmail/watch", notification, ct);
Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
Assert.Equal("123", (await received.NextAsync(ct: ct)).HistoryId);
```

`notification` is a valid Pub/Sub envelope constructed in the test from a base64-encoded Gmail notification, as in the existing webhook test. Keep input creation visible; the framework does not know Gmail payloads.

### Application example

```csharp
var ct = TestContext.Current.CancellationToken;
var options = new IntoChatOptions
{
    Flutter = new() { Hosting = new() { Kind = FlutterHostKind.Web } }
};
await using var brain =
    await E2EDigitalBrainSimulation.StartAsync<Projects.IntoChat_AppHost>(options, ct);
await brain.Get<IBitcoin>("btc").SetPrice(64_000);
await using var browser = await brain.OpenBrowserAsync(ct);

using var response = await brain.HttpClient.PostAsJsonAsync(
    "/twitter/webhook",
    new { Account = "elonmusk", Text = "Bitcoin to the moon" }, ct);
Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
await Assertions.Expect(browser.Page.GetByText(
    "elonmusk: Bitcoin to the moon  BTC 64000", new() { Exact = true }))
    .ToBeVisibleAsync();
```

Browser creation resolves the configured endpoint and navigates to it. Browser assertions receive a finite configured timeout; session cancellation closes the context so a canceled test cannot continue waiting indefinitely. Readiness guarantees the hosted behavior subscription before the webhook; no fixed delay or fake replay is allowed.

## 5. Typed module and application configuration

### Module definitions

Put the small production composition contracts in `src/Modules/DigitalBrain/DigitalBrain/Composition/`, not a testing package. A `ModuleDefinition` is an immutable snapshot containing a stable module identifier, module assembly/type identity, public configuration entries, secret references, hosting configuration, and declared module dependencies. No delegates, module instances, service providers, or arbitrary object graphs cross a process boundary.

Each module owns a typed factory, for example `GoogleModule.Define(GoogleModuleOptions options)` and `FlutterModule.Define(FlutterModuleOptions options)`. These return `ModuleDefinition`. This replaces the earlier illustrative `Module.Google(...)` spelling: a central static factory would force the core toolkit to depend on every module. Module-owned factories avoid that dependency.

Module factories own validation and configuration-key mapping; the framework never infers keys from property names. Runtime options and hosting options have distinct sections. Reuse existing leaf settings where possible; introduce module aggregate options only to compose those settings coherently. Unit execution validates the definition but ignores hosting projections and explicitly installs memory providers. Unit-only dependency overrides remain local callbacks in `UnitOptions`, never fields inside `ModuleDefinition`.

Composition expands explicit dependencies before startup. Equal repeated definitions deduplicate. Conflicting settings for one module ID, cycles, unavailable modules, and unknown configuration schema versions fail before any process starts. No silent last-writer-wins merge. A module with no extra dependencies remains a single registration.

### Application options

Create an application-owned configuration library under `src/Applications/IntoChat/Configuration/`. It references the selected modules' configuration contracts, not testing packages or the AppHost executable. `IntoChatOptions` contains typed aggregates for every module that IntoChat currently composes: Google, Flutter, Time, and the current Twitter demo module; do not pretend only the first three exist. Empty options for settings-free modules need not invent switches.

`IApplicationConfiguration` is a small production contract with `ApplicationConfigurationSnapshot CreateSnapshot()`. The snapshot identifies its schema/application and separates public configuration from secret references. IntoChat validates and maps its options; the harness transports them. The AppHost binds the same application configuration and creates the production module definitions. The runtime receives the resolved module configuration through production hosting projections.

Use complete immutable option values with explicit defaults, not nullable patch objects. Tests use `with` expressions to change nested records. Snapshot at startup so later mutation of collections cannot affect a running host. The AppHost's normal defaults and the options object's defaults must agree. Supplying typed options replaces the owned application configuration sections; ambient user secrets/environment must not silently fill missing test settings. Harness-owned endpoint/identity/storage values are applied separately and cannot be overwritten by application options.

Do not serialize existing credential-bearing `GmailOAuthOptions`. Introduce references to credentials; resolve values through the existing secret/provider path or a private run-scoped transport. Never put resolved secrets in command arguments, public manifests, option `ToString()`, diagnostic dumps, or committed artifacts. Failure messages identify the missing setting by name only.

## 6. Dependency structure and files

Four public packages, with supporting implementation projects where necessary:

```text
DigitalBrain.Testing.Unit ---------> DigitalBrain.Testing
DigitalBrain.Testing.Integration --> DigitalBrain.Testing
DigitalBrain.Testing.E2E ----------> DigitalBrain.Testing

Integration and E2E --> DigitalBrain.Testing.Hosting (internal support library)
Integration -------> shared ModuleAppHost + ModuleRunner build assets

All hosting paths --> production DigitalBrain composition/runtime/hosting code
```

`DigitalBrain.Testing` owns probes, behavior runs, bounded waits, shared session resource tracking, diagnostic contracts and common exceptions. It must not reference Aspire, Playwright, Orleans.TestingHost, xUnit, or a product module. Existing references to core runtime types may remain where required by the observation implementation.

`DigitalBrain.Testing.Hosting` is an implementation library for Aspire run lifetime, connection discovery, external-client setup, isolated execution policy, diagnostics and configuration transport. Its existence does not add another public testing layer. Unit never references it. E2E never references Integration or Unit.

`DigitalBrain.Testing.ModuleRunner` is a single executable referencing production runtime bootstrap. `DigitalBrain.Testing.ModuleAppHost` is a single framework-owned AppHost for selected module definitions and their hosting projections. Neither statically references every product module or IntoChat.

Do not merge application middleware into a new universal host. Extract reusable runtime registration and endpoint mapping; IntoChat retains its authentication, CORS, application endpoints, and behaviors. The runner uses the same module HTTP pipeline, including module HTTP surfaces where required.

## 7. External runner and build-time bundle

The module test project references its selected modules and required Aspire hosting adapters. Build produces a runner bundle containing the shared executable, selected modules, managed/native runtime assets, generated Orleans metadata and a versioned resolver manifest. `StartAsync` validates and launches an existing bundle; it never invokes restore/build or generates a project.

The infrastructure AppHost also needs the matching hosting adapter assemblies and dependency resolution. Bundle design must account for both host-side projections and runner-side runtime dependencies, keeping their load contexts separate.

Use normal shared identities for DigitalBrain contracts and Orleans assemblies; do not load private duplicate copies that make `IModule` casts or generated serializers incompatible. Reject ambiguous/conflicting shared versions with a useful diagnostic. Preserve managed dependencies and RID-specific native assets; flattening DLL filenames without collision checks is not sufficient.

The first implementation milestone must demonstrate:

1. A runner with no static Google reference loads Google from build output.
2. A remote test client calls `Get<IGmail>`, subscribes, and receives a serialized signal after a real HTTP webhook.
3. A second module with a transitive dependency works, including a representative native asset resolution check.
4. The hosting process loads its adapter independently.
5. Missing assets and shared-version conflicts fail before a misleading health timeout.
6. Cancellation and normal disposal leave no runner process or owned container.

Until this passes, do not migrate all projects or present the external runner as proven. If the bundle cannot reliably resolve dependencies with the pinned runtime, revise this design explicitly; do not silently replace external execution with in-process hosting. The shared in-process implementation remains a documented fallback, not an initial supported execution mode.

## 8. Startup, isolation and readiness

Startup sequence: validate/snapshot -> validate bundle -> acquire run ownership -> create graph -> start dependencies/runtime -> await resource health -> connect brain client -> await declared application readiness -> create HTTP client -> return brain.

Use one startup deadline across all phases, not a fresh three-minute timeout for every operation. Initial defaults: startup 3 minutes, assertion wait 5 seconds (UI assertions 60 seconds), cleanup 30 seconds. Keep these in `TestExecutionOptions` with validated overrides; transport handles the values without application-specific names. Every operation accepts cancellation. A failed factory owns rollback even when no brain was returned.

Each run owns a unique directory, cluster/service identity, network endpoints, external processes, and storage. Integration/E2E default to ephemeral test-owned storage without persistent volume reuse. Restart within a run preserves that run's storage. Ports and CORS origins come from discovered endpoints. Do not change the user's normal development persistence defaults.

Infrastructure health is not behavior readiness. Add production behavior status tracking keyed by behavior identity and run generation. IntoChat declares the source/signal subscriptions required before serving a scenario as ready. A behavior-scoped brain decorator records successful subscriptions and marks them unavailable when they close or a behavior restarts. Readiness is false while retrying; current generation failure is included in diagnostics. Aggregate this into a normal application readiness health check. Do not scrape log text as the readiness protocol.

An individual subscription completing proves only that subscription. Multi-subscription behaviors require all declared subscriptions. Unit `BehaviorRun.WaitForSubscriptionAsync` keeps its existing explicit semantics. Optional application behaviors need not block readiness; IntoChat must label its required behavior explicitly.

Remove fake tweet replay once readiness is wired. Signals remain live, bounded, non-replayed, and may be lost in the documented commit-to-publish gap. Framework reliability must not change product delivery semantics.

## 9. Cleanup and diagnostics

One idempotent session lifetime owns every acquired resource. Register ownership immediately after acquisition. On shutdown: cancel test-owned behavior work, capture requested failure artifacts before closing the browser, dispose observations/browser/HTTP/client, stop owned runtime, stop/dispose AppHost infrastructure, remove private run files. Attempt every stage even after failures. Graceful shutdown has a deadline; terminate only this run's external process tree if necessary. In-process unit tasks cannot be forcibly terminated; report uncooperative work.

Do not use the already-canceled test token as the cleanup token. Collect cleanup failures with stage names. Startup exceptions preserve the original cause plus rollback failures. With plain `await using`, a disposal exception can replace a test-body exception at the language level; do not claim to preserve both automatically. Persist structured cleanup diagnostics and retain runner output, and document that callers/framework adapters can aggregate body and cleanup exceptions if needed. No xUnit adapter is required initially.

On failure provide run ID, stage/deadline, selected module IDs, endpoint names, process exit/resource state, bounded logs, missing readiness requirements and paths to browser screenshot/trace when available. Never dump arbitrary payloads, credentials or unbounded histories. Factories accept a test-neutral diagnostic sink via execution options; tests may adapt xUnit output themselves.

Browser contexts close on cancellation; traces/screenshots are best-effort and bounded. Automatic capture on assertion failure cannot depend on xUnit internals: retain the final session trace/screenshot on disposal by configured policy, and always capture on driver/startup failures. Artifact collection failure must not prevent process cleanup.

## 10. Migration and acceptance

Move shared helpers first, then rename the unit package. Rename the old E2E package and replace its cast-based HTTP extension with concrete `E2EBrain.HttpClient`. Move Google HTTP tests to `src/Modules/Google/Integration/`; remove references from Google to IntoChat and from Google unit tests to any integration/E2E harness. Remove `ModuleWebHost` when its final caller has migrated.

Keep genuine unit Google tests in `Tests/`. Move the OAuth HTTP test to integration; its external token exchange uses a controlled provider endpoint if the production SDK supports that seam. If it does not, add and verify the production endpoint configuration seam before migration; do not claim a unit fake was transported remotely. Keep lower-level token-exchange logic tests in Unit.

IntoChat tests remain in their current application test project for this change but reference `.Testing.E2E`. Keep one application Gmail wiring smoke test only if its assertion is distinct from module webhook coverage. The Elon HTTP test verifies the full exact inbox text; the UI test verifies the visible result. Persistence/restart coverage uses Time or another stateful neuron, not the transient Twitter fake.

Split runner-only MSBuild defaults from layer dependencies. `Module.Tests.props` must not implicitly select Unit for every test project. Keep `DigitalBrain.Foundation.slnx` fast and container-free. Add a separate testing solution for framework integration and app E2E projects. Preserve pinned SDK `11.0.100-rc.1.26425.128`, target `net11.0`, and `xunit.v3.mtp-v2` version `4.0.0` unless an independently justified compatibility issue requires review.

Acceptance: unit tests run without Docker; Google integration runs without IntoChat/Flutter; E2E uses actual IntoChat and typed options; parallel runs cannot observe each other's state; canceled/failed startup leaves no owned resources; behavior readiness works without replay; real state survives runner restart; endpoints contain no fixed test ports; the four public packages have the stated dependency directions.

## 11. Sources and validation boundary

Repository files above were inspected; no implementation builds/tests were run for this design. Context7 was attempted but returned a quota error; official documentation was used as fallback.

- [Aspire resource access and health](https://aspire.dev/testing/accessing-resources/): resource-based HTTP clients, connection discovery, bounded health waits.
- [Aspire advanced testing](https://aspire.dev/testing/advanced-scenarios/): configuration-driven composition and file-based AppHost limitations.
- [xUnit lifecycle and fixtures](https://xunit.net/docs/shared-context.html): per-test class instances, async cleanup, explicit fixture sharing.
- [xUnit parallel execution](https://xunit.net/docs/running-tests-in-parallel): configure the repository's v3 4.0 scheduling deliberately.

The shared external bundle, typed configuration transport, and application readiness mechanism are proposed implementation work. Validate exact third-party APIs against pinned packages during implementation.
