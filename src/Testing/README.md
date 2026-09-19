# Testing

Three layers, never mixed:

**Neuron tests** (`DigitalBrain.NeuronTesting`): in-process Orleans via `DigitalBrainSimulation`. Grains, `PublishAsync`, `Observe<T>`. No HTTP, no Kestrel, no Docker. Time, kernel, Google neuron tests use this.

**Module e2e** (`DigitalBrain.E2ETesting.ModuleWebHost`): same in-process cluster **plus** a Kestrel host that maps `IModule.Configure(endpoints)`. Google webhook/auth stubs use this. Not Aspire.

**App e2e** (`DigitalBrain.E2ETesting.E2EDigitalBrain`): real Aspire AppHost, Orleans cluster, Azure Storage emulator in Docker. IntoChat uses this. Time has no e2e project.

Run neuron tests: `dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false`.

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

`DigitalBrainSimulation.StartAsync` returns the production `IDigitalBrain`; pass real module instances
through `Modules`. Replace provider dependencies through `ConfigureSilo`; keep provider-specific test
controls with the module. Simulation has no `UseHttp` and does not host Kestrel.

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
webhook. Neuron simulation never starts Kestrel. Module e2e starts `ModuleWebHost` on the same cluster:

```csharp
await using var brain = await DigitalBrainSimulation.StartAsync(new()
{
    Modules = [new GoogleModule()],
}, ct);
await using var web = await ModuleWebHost.StartAsync(brain.Cluster(), [new GoogleModule()], ct);
var response = await web.Client.PostAsJsonAsync("google/gmail/watch", payload, ct);
```

App e2e: `await using var app = await E2EDigitalBrain.StartAsync<Projects.IntoChat_AppHost>(ct);` then `CreateHttpClient` / `WaitHealthyAsync`. That path uses the AppHost graph (Azurite tables/blobs, real silo), not `InProcessTestCluster`.

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

