# Testing

Four public libraries share production module definitions and return concrete brains implementing
`IDigitalBrain`. Their execution boundaries remain explicit:

| Library | Purpose |
|---|---|
| `DigitalBrain.Testing` | Probes, behavior runs, bounded waits, execution options and lifetime |
| `.Unit` | In-process Orleans neuron/component tests with memory storage and controlled providers |
| `.Integration` | Selected modules in an external runtime, real HTTP and Aspire-managed ephemeral storage |
| `.E2E` | Selected modules or the actual application AppHost, with automatic browser startup when configured |

Plain function/object unit tests need no brain harness. Integration verifies module transport and
process/storage behavior; module E2E verifies UI behavior and product E2E verifies actual application wiring. Product E2E can run without a browser.

The solution places only these four libraries directly under Testing. Infrastructure contains the
shared Aspire session, ModuleRunner and ModuleAppHost. These remain physical projects, not additional
public testing layers. Harness invariants live next to Core, Testing, and E2E; a test-assembly module
loading through the runner is covered by Google integration.

## Neuron tests

```csharp
var ct = TestContext.Current.CancellationToken;
await using var brain = await UnitTest.Create()
    .WithModule<TimeModule>()
    .WithReminders()
    .WithExecution(new() { AssertionTimeout = TimeSpan.FromSeconds(3) })
    .StartAsync(ct);
var timer = brain.Get<DigitalBrain.Time.Timers.ITimer>("tea");
await using var ticks = await brain.Observe<TimerTick>(timer, ct);
await timer.Start(TimeSpan.Zero);
var tick = await ticks.NextAsync(ct: ct);
Assert.Equal("tea", tick.TimerId);
```

Use the same `WithModule<T>(options => ...)` declarations in AppHost, Unit and Integration.
Module-owned methods such as `RunWebApp`, `WithPostgres` and `WithDefaultLlm<T>` expose their
choices. `WithOptions(value)` explicitly replaces a module's complete options. `ConfigureModule<T>`
changes an existing declaration; it cannot add a missing module. There is no application-wide
configuration class with a property for each module.

Declarations are copied and resolved in dependency order. Duplicate explicit modules and
conflicting shared settings fail early. Aspire materializes module resources once, on the first
runtime or client reference. Later configuration throws. Each test builder starts one session.
Typed AI declarations reject API keys instead of dropping them. Supply credentials through
private configuration or Aspire secret parameters; public module overrides never transport them.

Use `ConfigureSilo` and `ConfigureClient` for local callbacks and controlled providers. These
callbacks are local; they do not cross a process boundary. Native Orleans timers/reminders remain
explicit dependencies. Unit hosting does not start HTTP or Docker.

`DeactivateAsync` tests activation lifetime; `RestartSiloAsync` restarts the in-process silo.
Neither proves persistence through external process death. These capabilities belong to UnitBrain.

## Module integration

```csharp
await using var brain = await IntegrationTest.Create()
    .WithModule<FlutterModule>(flutter => flutter.BackendOnly())
    .StartAsync(ct);

var button = brain.Get<IButton>("go");
await using var clicks = await brain.Observe<ButtonClicked>(button, ct);
using var response = await brain.HttpClient.PostAsJsonAsync(
    "/ui/buttons/go/click", new { }, ct);
Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
await clicks.NextAsync(ct: ct);
```

The runner loads the test build's dependency closure; it never builds/restores at test startup.
Import `Integration.Tests.props`. Include the selected module's Aspire hosting adapter project
when that module declares one. The module host locates the Flutter shell from the repository root; an explicit working directory can override it.

Independent runs own independent infrastructure. Use HTTP stubs and typed endpoint overrides for
external providers; use a compiled test support module when the replacement is an in-process
interface inside the external runtime.

GoogleModuleOptions includes PublicOrigin and TokenEndpoint. Credential-bearing values belong in
`WithExecution(new() { PrivateConfiguration = ... })`, outside public module options:

```csharp
var execution = new TestExecutionOptions
{
    PrivateConfiguration = new Dictionary<string, string?>
    {
        ["DigitalBrain:Google:Gmail:OAuth:ClientId"] = "integration-client",
        ["DigitalBrain:Google:Gmail:OAuth:ClientSecret"] = "integration-secret",
    },
};
```

Hosted runs transfer these values through an ACL-restricted temporary file, expose only its path
to the primary runtime, and delete it on rollback/disposal. Unit applies them locally.
Use synthetic credentials for protocol stubs. Test-owned identity/connections cannot be overridden.

## Module and application E2E

```csharp
// Flutter owns its module E2E defaults: web frontend + headless browser.
await using var brain = await E2ETest.Create()
    .WithModule<FlutterModule>()
    .StartAsync(ct);
await Assertions.Expect(brain.Page.GetByText("Expected content")).ToBeVisibleAsync();

// IntoChat owns its product defaults; the native builder supports explicit overrides.
await using var product = await IntoChatE2ETest.Create()
    .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp(browser => browser.Headed().SlowMo(250)))
    .StartAsync(ct);
```

Selected modules provide `IModuleE2EDefaults<TModule>`. The framework applies these before the
caller's callback, so `.WithModule<FlutterModule>(f => f.BackendOnly())` overrides web hosting.
`DigitalBrain.Modules.Flutter.Testing` supplies the browser callback overload of `RunWebApp`;
its `BrowserConfiguration` stays in the test process, outside production module options.

`IntoChatE2ETest.StartAsync(ct)` uses the actual `E2ETest.For<Projects.IntoChat_AppHost>()`
and defaults to backend-only execution with disposable Qdrant, ClickHouse and PostgreSQL.
Its configuration is in `Applications/IntoChat/Tests/E2E/IntoChatE2ETest.cs`. Scenarios own seed
rows and any scripted model endpoint separately. Unused external providers point to an
unavailable local endpoint, so ordinary tests cannot accidentally call live services.

Application builders preserve AppHost declarations. `ConfigureModule<T>` patches an existing
module; it cannot add a missing one. Overrides preserve unassigned application fields,
including explicit false/null/default assignments. No delegates cross a process boundary.

| Flutter call | Meaning |
|---|---|
| `BackendOnly()` | Neurons and HTTP endpoints; no Flutter frontend process or browser |
| `RunWebApp()` | Flutter web frontend; E2E automatically opens a headless browser |
| `RunWebApp(b => b.Headed().SlowMo(250))` | Same web frontend in a visible browser with delayed browser actions |
| `RunDesktopApp()` | Native Flutter desktop frontend; no browser |

The obsolete pure-Dart console host is removed. Flutter VM/DDS hot reload remains supported.
`SlowMo` affects browser calls, not backend execution. A visible browser proves only what a
scenario explicitly asserts. Database-to-UI scenarios use typed table/workspace setup; agent
journeys submit real browser messages and are kept separate.

E2E builders default to headless even with a debugger attached. Explicit `Headed()` or
`WithBrowser(new() { Headless = false })` opts into visibility. `BrowserOptions` with unspecified
fields can use `DIGITALBRAIN_E2E_HEADED`/debugger fallback. One brain owns its automatically opened
`Page`; `OpenBrowserAsync` remains available for additional isolated sessions. Do not open a
second browser just to drive the default page. Startup failure disposes both browser and host.

Flutter advertises its application's `semantics=true` query and semantics-tree readiness selector
through browser endpoint metadata. Generic E2E contains no Flutter-specific selectors.
Readiness does not wait for the transient accessibility activation placeholder. Resource health,
frontend readiness and the scenario's own data/subscription readiness are separate conditions.
The Flutter web-server host also waits for the current compilation's completion log before
becoming healthy: an HTTP response can otherwise serve a cached build with an old runtime URL.
This adapter follows the pinned Flutter CLI output and must be updated if that output changes.

## Budgets, observations and cleanup

All three layers accept WithExecution with defaults: startup 3 minutes, signal/behavior assertion
5 seconds, cleanup 30 seconds. These are per-run values, not global settings.
BrowserOptions separately defaults startup to 2 minutes and page actions to 60 seconds.
Native Playwright assertions may supply their own timeout explicitly.

Subscribe before triggering an action. `RunBehavior` tracks errors and cancellation; await
`WaitForSubscriptionAsync<T>` before publishing. Signals are live and bounded, never replayed.
TestWait bounds reads without retrying the action under test.

Owned resources are disposed in reverse order under one session cleanup budget. Cleanup failures
are aggregated and later resources are still attempted. A noncooperative in-process callback cannot
be forcibly terminated; failures are surfaced rather than silently swallowed.

E2E saves screenshots, traces and bounded diagnostic categories under its artifact directory.
Screenshot failure does not skip tracing. Cancellation closes the browser context and unblocks
pending waits. Artifacts can contain application content; use controlled fixtures.

## Running and packaging status

```powershell
dotnet test --project src/Modules/Flutter/Tests/Unit/DigitalBrain.Modules.Flutter.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Flutter/Tests/Integration/DigitalBrain.Modules.Flutter.Tests.Integration.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Flutter/Tests/E2E/DigitalBrain.Modules.Flutter.Tests.E2E.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj -p:CodeGraphRefresh=false
dotnet test --solution DigitalBrain.slnx --max-parallel-test-modules 1 -p:CodeGraphRefresh=false
```

Current support is verified through repository project references. Removing ModuleAppHost is gated:
the programmatic builder probe failed because its caller lacks Aspire AppHost SDK metadata.
The existing NuGet pack path also failed NU5039 (missing packaged README). External package
consumption and Linux/native-asset portability are not certified by this refactor. Retaining the
shared host/runner preserves process isolation while those packaging concerns remain explicit.

The implementation record is in
[code-first-execution-progress.md](../../docs/superpowers/plans/code-first-execution-progress.md).

## Test ownership and product journeys

```text
src/Modules/Flutter/Tests/
  Unit/<component>/           neuron logic and signals
  Integration/<component>/    HTTP, transport and neuron flow
  E2E/<feature>/              real rendering and browser gestures
src/Modules/Supabase/Tests/Unit/
src/Applications/IntoChat/Tests/E2E/
  Composition/               one startup smoke test
  Workspace/                 data display, isolation, restore and operation recovery
  Agent/                     protocol workflows and small complete browser journeys
  Inbox/                     composed webhook-to-inbox workflows
```

IntoChat has no Unit or Integration test project. Product E2E asserts connected product behavior;
module suites own the constituent behavior. Widget tests cover UI controls in isolation. A UI
E2E can use typed setup, then assert only rendered outcomes; it need not reassert neuron signals.
Generic configuration, override transport and host isolation checks belong to framework tests.

`SupabaseTableDisplayFacts` seeds the deployment's temporary PostgreSQL, creates a real project
through the browser, and opens an `ISupabaseTable` through `IWorkspace`. It requires no agent or
model. `WorkspaceRestoreFacts` covers workspace isolation and restored filtered views.
`AgentTableJourneyFacts` separately sends a real chat message through the Flutter shell, uses a
scripted OpenAI protocol endpoint, and checks the rendered table and cancellation. The scripted
server owns only its protocol; the test owns the app and data.

For paid live coverage, set `DIGITALBRAIN_E2E_LIVE_MODEL=1` and
`DIGITALBRAIN_E2E_MODEL_API_KEY`, then select `*LiveAgentTableJourneyFacts`.
`DIGITALBRAIN_E2E_MODEL_ENDPOINT` optionally selects a compatible endpoint. Without opt-in,
the live test is skipped. Missing credentials fail explicitly.

Run the solution with `--max-parallel-test-modules 1` to avoid competing Flutter compiler
processes against the shared checkout. Browser assemblies also serialize their browser tests.
CI installs Flutter and Chromium, runs .NET tests and Flutter UI/shell tests, and disables live
model calls by default.

Migration decisions and validation results:
[testing-architecture.md](../../docs/superpowers/plans/2026-09-21-testing-architecture.md).
