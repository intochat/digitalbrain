# Testing the migrated foundation

Run `dotnet test --solution DigitalBrain.Foundation.slnx -p:CodeGraphRefresh=false`.
This partial solution covers the live runtime, reusable test host and Time module.
The original full solution still contains unmigrated consumers.

## Test a real module

```csharp
var ct = TestContext.Current.CancellationToken;
await using var host = await BrainTestHost.StartAsync(new()
{
    UseReminders = true,
    ConfigureSilo = silo => silo.AddTime()
}, ct);
var timer = host.Brain.Get<DigitalBrain.Time.ITimer>("tea");
await using var events = await host.ObserveAsync<TimerElapsed>(timer, ct);
var scheduled = await timer.Schedule(1, "tea"); // Subscribe first; trigger once.
var elapsed = await events.NextAsync(ct: ct);
Assert.Equal(scheduled.Generation, elapsed.Generation);
Assert.Equal(TimerStatus.Elapsed, (await timer.Read()).Status);
```

Use the actual module registration and production `BrainClient`. Replace provider dependencies
through `ConfigureSilo`; keep provider-specific test controls with the module. The common harness
contains no timer, AI, HTTP, or command/reaction simulator.

`RunBehavior` owns cancellation and observes errors. Await `run.WaitForSubscriptionAsync<T>(source, ct)`
before triggering work: readiness belongs to that run, source identity and signal type. Early failures
surface immediately. `SignalProbe` has one reader, bounded diagnostics and a five-second wait. For
real reminders, use the subscription directly and a 90-second outer deadline (see `TimerReminderFacts`).
Most tests use controlled domain time to exercise the production due handler; that does not advance
the Orleans scheduler. Keep the small actual-reminder suite to validate real wiring.

`TestWait.UntilAsync` bounds each read and the overall deadline. It never retries the action under
test. Timeout output omits complex payloads. Teardown cancels behaviors, joins them with a five-second
deadline, disposes observations and the client, then stops Orleans. A behavior that ignores cancellation
is reported as a cleanup failure; an in-process task cannot be forcibly terminated.

## State recovery and failures

Set `PersistenceDirectory` to a test-owned temporary directory. Dispose a host before opening the
same directory in another host; concurrent ownership is rejected. Reuse the grain ID and state name.
`UseReminders` also persists reminder rows there. Tests own deletion of explicit directories; the host
removes its internally allocated fault-test directory. This is a single-process serialized test adapter,
not proof of distributed storage consistency or hard-crash durability.

Pass `StorageFaults` to refuse the next read/write for a specific `GrainId`, or use `HoldNextRead` and
await its `Entered` task. Release holds explicitly; teardown also releases outstanding holds. Orleans
wraps provider exceptions in `OrleansException`. Module code must reload after a refused write and
stop the activation if recovery fails. The harness does not implement that domain policy for modules.

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

