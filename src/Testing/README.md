# Testing

Four public libraries share production module definitions and return concrete brains implementing
`IDigitalBrain`. Their execution boundaries remain explicit:

| Library | Purpose |
|---|---|
| `DigitalBrain.Testing` | Probes, behavior runs, bounded waits, execution options and lifetime |
| `.Unit` | In-process Orleans neuron/component tests with memory storage and controlled providers |
| `.Integration` | Selected modules in an external runtime, real HTTP and Aspire-managed ephemeral storage |
| `.E2E` | Actual application AppHost, application scenarios and optional browser driving |

Plain function/object unit tests need no brain harness. Integration verifies module transport and
process/storage behavior; E2E verifies actual application wiring. A browser is optional for E2E.

The solution places only these four libraries directly under Testing. Infrastructure contains the
shared Aspire session, ModuleRunner and ModuleAppHost; Tests contains framework verification.
These remain physical projects, not additional public testing layers.

## Neuron tests

```csharp
var ct = TestContext.Current.CancellationToken;
await using var brain = await UnitTest.StartAsync(new()
{
    Modules = [TimeModule.Define()],
    UseReminders = true,
    Execution = new() { AssertionTimeout = TimeSpan.FromSeconds(3) },
}, ct);
var timer = brain.Get<DigitalBrain.Time.Timers.ITimer>("tea");
await using var ticks = await brain.Observe<TimerTick>(timer, ct);
await timer.Start(TimeSpan.Zero);
var tick = await ticks.NextAsync(ct: ct);
Assert.Equal("tea", tick.TimerId);
```

Use module-owned `Define` factories for typed settings. A settings-free module can use
`new ModuleDefinition(typeof(MyModule))`. Definitions are copied, resolved in dependency order
and configured once. Conflicting repeated definitions or shared configuration keys fail early.

Aspire's parameterless `AddModule<T>()` applies the module's hosting defaults.
An explicit `AddModule<T>(callback)` gives that callback control of hosting; automatic defaults
are not applied before it.

Use `ConfigureSilo` and `ConfigureClient` for local callbacks and controlled providers. These
callbacks are local; they do not cross a process boundary. Native Orleans timers/reminders remain
explicit dependencies. Unit hosting does not start HTTP or Docker.

`DeactivateAsync` tests activation lifetime; `RestartSiloAsync` restarts the in-process silo.
Neither proves persistence through external process death. These capabilities belong to UnitBrain.

## Module integration

```csharp
await using var brain = await IntegrationTest.StartAsync(new()
{
    Modules = [FlutterModule.Define(new()
    {
        Hosting = new() { Kind = FlutterHostKind.None },
    })],
}, ct);

var button = brain.Get<IButton>("go");
await using var clicks = await brain.Observe<ButtonClicked>(button, ct);
using var response = await brain.HttpClient.PostAsJsonAsync(
    "/ui/buttons/go/click", new { }, ct);
Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
await clicks.NextAsync(ct: ct);
```

The runner loads the test build's dependency closure; it never builds/restores at test startup.
Import `Integration.Tests.props`. Include the selected module's Aspire hosting adapter project
when that module declares one (Flutter does). For frontend hosts outside an application AppHost,
set an explicit Flutter working directory appropriate to the checkout.

`RestartRuntimeAsync` retains the run's storage and identity. Reacquire observations after restart.
Independent runs own independent infrastructure. Use HTTP stubs and typed endpoint overrides for
external providers; use a compiled test support module when the replacement is an in-process
interface inside the external runtime. The framework's provider-loading test exercises this case.

GoogleModuleOptions includes PublicOrigin and TokenEndpoint. Credential-bearing values belong in
`Execution.PrivateConfiguration`, not ModuleDefinition:

```csharp
Execution = new()
{
    PrivateConfiguration = new Dictionary<string, string?>
    {
        ["DigitalBrain:Google:Gmail:OAuth:ClientId"] = "integration-client",
        ["DigitalBrain:Google:Gmail:OAuth:ClientSecret"] = "integration-secret",
    },
}
```

Hosted runs transfer these values through an ACL-restricted temporary file, expose only its path
to the primary runtime, and delete it on rollback/disposal. Unit applies them locally.
Use synthetic credentials for protocol stubs. Test-owned identity/connections cannot be overridden.

## Application E2E and visible browsers

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
await Assertions.Expect(browser.Page.GetByText("Expected content")).ToBeVisibleAsync();
```

Application configuration is required. IntoChat's normal default remains Window; E2E does not
silently change it. Select Web for browser tests or None for HTTP-only application tests.
The application snapshot validates exact module slots and public setting keys against the AppHost's
declared defaults. Unknown/missing slots fail rather than being silently ignored. Module selection
and hosting projections share `AddModules`; arbitrary subsets belong to Integration.

| Flutter hosting | Meaning |
|---|---|
| Web | Web frontend, driven by either a headed or headless browser |
| Window | Native desktop host |
| Headless | Pure-Dart host; not a hidden web browser |
| None | No frontend host; neurons and module HTTP can still run |

Browser options live only in E2E. Explicit Headless/SlowMo values win over the environment/debugger.
Otherwise `DIGITALBRAIN_E2E_HEADED=1` or an attached debugger requests a visible browser with 250 ms
slow motion; unattended defaults are headless with no delay. Explicit zero delay is respected.
One brain owns one launch configuration; each OpenBrowserAsync creates an isolated browser context.

Flutter advertises its application's `semantics=true` query and semantics-tree readiness selector
through browser endpoint metadata. Generic E2E contains no Flutter-specific selectors.
Readiness does not wait for the transient accessibility activation placeholder. Resource health,
frontend readiness and the scenario's own data/subscription readiness are separate conditions.

## Budgets, observations and cleanup

All three layers accept Execution with defaults: startup 3 minutes, signal/behavior assertion
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
dotnet test --project src/Modules/Flutter/Tests.Unit/DigitalBrain.Modules.Flutter.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Flutter/Tests.Integration/DigitalBrain.Modules.Flutter.Tests.Integration.csproj -p:CodeGraphRefresh=false
dotnet test --solution DigitalBrain.slnx -p:CodeGraphRefresh=false
```

Current support is verified through repository project references. Removing ModuleAppHost is gated:
the programmatic builder probe failed because its caller lacks Aspire AppHost SDK metadata.
The existing NuGet pack path also failed NU5039 (missing packaged README). External package
consumption and Linux/native-asset portability are not certified by this refactor. Retaining the
shared host/runner preserves process isolation while those packaging concerns remain explicit.

The implementation record is in
[testing-refactoring-progress.md](../../docs/superpowers/plans/testing-refactoring-progress.md).
