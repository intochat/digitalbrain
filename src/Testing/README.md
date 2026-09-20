# Testing

Three layers, never mixed. Each has its own public package and returns a concrete brain that implements the production `IDigitalBrain`.

**Unit tests** (`DigitalBrain.Testing.Unit`): `DigitalBrainSimulation.StartAsync(UnitOptions)` runs the lightweight in-process Orleans test cluster with memory storage. Grains, `PublishAsync`, `Observe<T>`. No HTTP, no Docker. Returns `UnitBrain`; unit-only lifecycle operations (`DeactivateAsync`, `RestartSiloAsync`) are extensions on `UnitBrain`, never on arbitrary brains.

**Module integration** (`DigitalBrain.Testing.Integration`): `ModuleDigitalBrainSimulation.StartAsync(IntegrationOptions)` selects module definitions and starts one shared external module runner with Aspire-managed, isolated, ephemeral infrastructure. Returns `IntegrationBrain` with `HttpClient` and `RestartRuntimeAsync`. No dependency on an application such as IntoChat.

**Application e2e** (`DigitalBrain.Testing.E2E`): `E2EDigitalBrainSimulation.StartAsync<TAppHost>(IApplicationConfiguration)` starts the application's actual AppHost with typed options. Returns `E2EBrain` with `HttpClient` and `OpenBrowserAsync`. No named test profiles.

Shared probes, behavior runs, bounded waits, session lifetime and diagnostics live in `DigitalBrain.Testing`.

Run unit tests: `dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false`.
Run the framework and application lanes: `dotnet test --solution DigitalBrain.Testing.slnx -p:CodeGraphRefresh=false`.

## Test a real module

```csharp
var ct = TestContext.Current.CancellationToken;
await using var brain = await DigitalBrainSimulation.StartAsync(new()
{
    Modules = [new TimeModule()],
    UseReminders = true,
}, ct);
var timer = brain.Get<DigitalBrain.Time.Timers.ITimer>("tea");
await using var events = await brain.Observe<TimerTick>(timer, ct);
await timer.Start(TimeSpan.Zero); // Subscribe first; trigger once.
var tick = await events.NextAsync(ct: ct);
Assert.Equal("tea", tick.TimerId);
```

`DigitalBrainSimulation.StartAsync` returns a `UnitBrain` that implements the production `IDigitalBrain`; pass
real module instances through `Modules`. Replace provider dependencies through `ConfigureSilo`; keep
provider-specific test controls with the module. Unit simulation has no `UseHttp` and does not host Kestrel.

`brain.RunBehavior` owns cancellation and observes errors. Await `run.WaitForSubscriptionAsync<T>(source, ct)`
before triggering work: readiness belongs to that run, source identity and signal type. Early failures
surface immediately. `SignalProbe` has one reader, bounded diagnostics and a five-second wait. For
long-running scenarios, use the subscription directly with an explicit outer deadline. Time tests use
controlled delivery through real Orleans timers, plus native timer/reminder scenarios. Their controlled
timestamps do not advance the Orleans scheduler. Reminder integration tests explicitly lower the native
minimum period; production retains the native one-minute default.

`brain.DeactivateAsync(neuron, ct)` awaits activation teardown through the native test cluster. Use it to
verify module lifetime without adding test-only methods to production contracts. An active timer is
discarded; a persistent reminder can autonomously reactivate its neuron on a later tick.
`brain.RestartSiloAsync(ct)` restarts the cluster silo; `brain.Client()` exposes the production client so
a test can exercise client disposal without stopping the silo.

`TestWait.UntilAsync` bounds each read and the overall deadline. It never retries the action under
test. Timeout output omits complex payloads. Teardown cancels behaviors, joins them with a five-second
deadline, disposes observations and the client, then stops Orleans. A behavior that ignores cancellation
is reported as a cleanup failure; an in-process task cannot be forcibly terminated.

## Module endpoints

A module may map HTTP endpoints by overriding `IModule.Configure(IEndpointRouteBuilder)`, for example a
webhook. Unit simulation never starts Kestrel. Module integration starts the shared external runner and
drives the module over real HTTP:

```csharp
var ct = TestContext.Current.CancellationToken;
await using var brain = await ModuleDigitalBrainSimulation.StartAsync(
    new() { Modules = [GoogleModule.Define(new())] }, ct);
var gmail = brain.Get<IGmail>("user@gmail.com");
await using var mail = await brain.Observe<MailReceived>(gmail, ct);
using var response = await brain.HttpClient.PostAsJsonAsync(
    "/google/gmail/watch", payload, ct);
Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
Assert.Equal("123", (await mail.NextAsync(ct: ct)).HistoryId);
```

Application e2e: `await using var brain = await E2EDigitalBrainSimulation.StartAsync<Projects.IntoChat_AppHost>(options, ct);`
then `brain.HttpClient` and `brain.OpenBrowserAsync()`. That path uses the AppHost graph (Azurite
tables/blobs, real silo), not `InProcessTestCluster`. Flutter web e2e opts into the semantics DOM with
`?semantics=true` so Playwright's native text locators can see canvas-rendered content.

## State recovery and failures

Neuron tests use Orleans memory storage; there is no file grain store.

Only committed module state persists. Signals are live, bounded, and never replayed. Overflow, failed
renewal or a changed activation faults the subscription. Behaviors may restart and subscribe again;
there is no automatic retry, resume cursor, inbox or outbox. A crash after a state commit but before
publication may lose the fact. Tests cover that allowed gap independently of any file-backed grain store.

## File-based behavior

`src/Behaviors/TimerReport.cs` is the same behavior linked into tests and included by the production
file app. The app references only production runtime and Time contracts; it starts an Orleans client.

```powershell
dotnet build src/Behaviors/timer-report.cs -p:CodeGraphRefresh=false
dotnet run --file src/Behaviors/timer-report.cs -p:CodeGraphRefresh=false -- --TimerId tea
```

The command above uses Orleans localhost development defaults. For a separate silo, supply
`--Gateways "gwy.tcp://127.0.0.1:30000/0" --ClusterId your-cluster --ServiceId your-service` with its
actual values. `--Smoke true` reports one fact then exits successfully. The process test supplies
the TCP test cluster's allocated gateway and cluster IDs, awaits the production `SubscriptionReady` log, schedules once, checks output
and exit status, and always cleans up the child. Unhandled behavior failures exit nonzero.

File directives and explicit `--file` execution follow the pinned SDK and
[Microsoft file-based app documentation](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps).

