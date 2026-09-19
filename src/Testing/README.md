# Testing the migrated foundation

Run `dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false`.
This partial solution covers the live runtime, reusable simulation and Time module.
The original full solution still contains unmigrated consumers.

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
controls with the module. The common simulation contains no timer, AI, HTTP, or command/reaction fake.

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

## State recovery and failures

Set `PersistenceDirectory` to a test-owned temporary directory. Dispose a simulation before opening the
same directory in another simulation; concurrent ownership is rejected. Reuse the grain ID and state name.
`UseReminders` also persists reminder rows there. Tests own deletion of explicit directories; the simulation
removes its internally allocated fault-test directory. This is a single-process serialized test adapter,
not proof of distributed storage consistency or hard-crash durability.

Pass `StorageFaults` to refuse the next read/write for a specific `GrainId`, or use `HoldNextRead` and
await its `Entered` task. Release holds explicitly; teardown also releases outstanding holds. Orleans
wraps provider exceptions in `OrleansException`. Module code must reload after a refused write and
stop the activation if recovery fails. The simulation does not implement that domain policy for modules.

Only committed module state persists. Signals are live, bounded, and never replayed. Overflow, failed
renewal or a changed activation faults the subscription. Behaviors may restart and subscribe again;
there is no automatic retry, resume cursor, inbox or outbox. A crash after a state commit but before
publication may lose the fact. Tests cover that allowed gap and state recovery independently.

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

