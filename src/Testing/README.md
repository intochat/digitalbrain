# Testing

Three packages. The only boundary that needs a project is **where the brain runs**; whether a test
drives typed neurons, HTTP or a browser is configuration.

| Package | The brain runs | Brings |
|---|---|---|
| `DigitalBrain.Testing` | — | probes, behavior runs, bounded waits, session lifetime, execution options |
| `DigitalBrain.Testing.Unit` | in the test process | Orleans `InProcessTestCluster`, memory storage |
| `DigitalBrain.Testing.E2E` | in a real process | Aspire-managed disposable storage, real HTTP, Playwright |

`.Unit` and `.E2E` never reference each other, and `.Unit` pulls no Aspire. Plain function and object
tests need no harness at all.

## Test projects

A test project is named `<Owner>.Tests.Unit` or `<Owner>.Tests.E2E` and lives in `Tests/Unit/` or
`Tests/E2E/` next to what it tests. It declares only its references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../../../../Testing/DigitalBrain.Testing.Unit/DigitalBrain.Testing.Unit.csproj" />
    <ProjectReference Include="../../Time/DigitalBrain.Modules.Time.csproj" />
  </ItemGroup>
</Project>
```

Everything else — `OutputType`, `IsTestProject`, the Microsoft.Testing.Platform runner, `xunit.v3.mtp-v2`
and the global usings — comes from the repository's `Directory.Build.props`, keyed on the `.Tests`
name. Do not restate it per project. This is a repository convention, not part of the packages:
a consumer outside this repo picks their own test runner and project shape, and receives only the
Aspire orchestration paths, from `DigitalBrain.Testing.E2E`'s `buildTransitive`.

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
Assert.Equal("tea", (await ticks.NextAsync(ct: ct)).TimerId);
```

`ConfigureSilo` and `ConfigureClient` install local callbacks and controlled providers; these do not
cross a process boundary. `DeactivateAsync` exercises activation lifetime and `RestartSiloAsync`
restarts the in-process silo — neither proves persistence through external process death.

## Hosted tests

The same builder covers HTTP-only and browser scenarios. A browser opens only when a module declares
a frontend, so the module's own hosting call is the switch:

```csharp
// HTTP only.
await using var brain = await E2ETest.Create()
    .WithModule<FlutterModule>(flutter => flutter.BackendOnly())
    .StartAsync(ct);
var button = brain.Get<IButton>(UiScope.Key("workspace-a", "go"));
await using var clicks = await brain.Observe<ButtonClicked>(button, ct);
using var response = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/ui/buttons/go/click", new { }, ct);
Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
await clicks.NextAsync(ct: ct);

// Frontend: E2E opens the page itself.
await using var app = await E2ETest.Create()
    .WithModule<FlutterModule>(flutter => flutter.RunWebApp())
    .StartAsync(ct);
await Assertions.Expect(app.Page.GetByText("Expected content")).ToBeVisibleAsync();
```

There is no module AppHost project. `ModuleTestHost` builds the Aspire application model in the test
process and relaunches **the test assembly itself** as the module runtime, against the test's own
`.deps.json` and `.runtimeconfig.json`. Reference the module under test and its `*.Aspire.Hosting`
adapter when it declares one — both are resolved reflectively inside the test process.

`Directory.Build.targets` stamps the Aspire DCP and dashboard paths onto every `*.Tests.E2E`
assembly, resolved from the NuGet package root for the current RID. Those paths are machine-local
and are never baked into a published package.

Independent runs own independent infrastructure. Use HTTP stubs and typed endpoint overrides for
external providers; use a compiled test support module when the replacement is an in-process
interface inside the external runtime.

## Application end-to-end

```csharp
await using var product = await IntoChatE2ETest.Create()
    .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp(browser => browser.Headed().SlowMo(250)))
    .StartAsync(ct);
```

`IntoChatE2ETest` uses `E2ETest.For<Projects.IntoChat_AppHost>()` and defaults to backend-only
execution with disposable Qdrant, ClickHouse and PostgreSQL. `ConfigureModule<T>` patches an existing
declaration and cannot add a missing one; overrides preserve unassigned fields, including explicit
`false`/`null` assignments. No delegates cross the process boundary.

`DigitalBrain.Modules.Flutter.Testing` supplies the browser-callback overload of `RunWebApp`; its
`BrowserConfiguration` stays in the test process, outside production module options. Generic E2E
contains no Flutter-specific selectors — Flutter advertises its readiness selector through browser
endpoint metadata.

E2E defaults to headless even with a debugger attached. `Headed()`, `WithBrowser(...)` or
`DIGITALBRAIN_E2E_HEADED=1` opt into visibility. One brain owns its automatically opened `Page`;
`OpenBrowserAsync` creates additional isolated sessions.

## Credentials

Credential-bearing values never travel through public module options — `ModuleSettingsValidation`
rejects keys containing `Secret` or ending in `Password`/`ApiKey`/`AccessToken`/`RefreshToken`.

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

Hosted runs write these to an ACL-restricted temporary file, expose only its path to the runtime, and
delete it on rollback or disposal. Unit applies them locally. Test-owned identity and connection
settings cannot be overridden.

## Budgets, observations and cleanup

`WithExecution` defaults: startup 3 minutes, signal and behavior assertions 5 seconds, cleanup 30
seconds — per run, not global. `BrowserOptions` separately defaults startup to 2 minutes and page
actions to 60 seconds.

Subscribe before triggering an action. `RunBehavior` tracks errors and cancellation; await
`WaitForSubscriptionAsync<T>` before publishing. Signals are live and bounded, never replayed.
`TestWait` bounds reads without retrying the action under test.

Owned resources are released in reverse acquisition order under one cleanup budget. Failures are
aggregated and later resources are still attempted; a noncooperative in-process callback cannot be
forcibly terminated, so its failure is surfaced rather than swallowed.

E2E writes screenshots, traces and bounded diagnostic categories to its artifact directory. These can
contain application content — use controlled fixtures.

## Running

```powershell
dotnet test --project src/Modules/Time/Tests/Unit/DigitalBrain.Modules.Time.Tests.Unit.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Google/Flutter/Tests/E2E/DigitalBrain.Modules.Flutter.Tests.E2E.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj -p:CodeGraphRefresh=false
dotnet test --solution DigitalBrain.slnx --max-parallel-test-modules 1 -p:CodeGraphRefresh=false
```

Hosted tests need a container runtime. Browser scenarios need Chromium:
`pwsh src/Modules/Google/Flutter/Tests/E2E/bin/Debug/net11.0/playwright.ps1 install chromium`.

Run the solution with `--max-parallel-test-modules 1` so Flutter compilers do not compete over the
shared checkout; browser assemblies also serialize their own browser tests.

For paid live coverage set `DIGITALBRAIN_E2E_LIVE_MODEL=1` and `DIGITALBRAIN_E2E_MODEL_API_KEY`, then
select `*LiveAgentTableJourneyFacts`. Without opt-in the live test is skipped; missing credentials
fail explicitly.

## Ownership

```text
src/Modules/<Module>/Tests/Unit/      neuron logic and signals
src/Modules/<Module>/Tests/E2E/       transport, process behavior and rendering
src/Applications/IntoChat/Tests/Unit/ application code that needs no host at all
src/Applications/IntoChat/Tests/E2E/  connected product behavior
```

IntoChat owns almost no behavior tests: module suites own the constituent behavior and product E2E
asserts that the composition works. Its unit suite is only for application code a host cannot make
more true — image header parsing, edit geometry, the local file store — which would otherwise pay
for Docker and a browser to assert a pure function. Flutter widget tests cover UI controls in
isolation.
