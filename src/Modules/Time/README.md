# Time

Time exposes two neurons with one schedule per grain identity. File-based behaviors subscribe to their typed live signals and decide what work a tick means.

```csharp
var timer = brain.Get<DigitalBrain.Time.Timers.ITimer>("tea");
await using var ticks = await brain.SubscribeAsync<TimerTick>(timer, ct);
await timer.Start(TimeSpan.FromSeconds(30));
await foreach (var tick in ticks.ReadAllAsync(ct))
{
    Console.WriteLine($"{tick.TimerId}: {tick.ObservedAt:O}");
    break;
}
```

| Contract | Start | Signal | Lifetime |
| --- | --- | --- | --- |
| ITimer | `Start(dueTime, period: null)` for one shot; positive period for repetition | TimerTick | Current activation |
| IReminder | `Start(dueTime, period)` | ReminderTick | Persisted Orleans reminder registration |

Both provide idempotent `Stop()`. Start replaces the existing schedule. Subscribe before starting if the first tick matters. Disposing a subscription does not stop a schedule: explicitly Stop periodic work when finished.

## Timers

TimerNeuron uses `RegisterGrainTimer`. Callbacks do not interleave with other grain calls. A periodic interval starts after the preceding callback completes, so callback work affects cadence. Timers do not promise exact wall-clock timing or submillisecond accuracy.

Due time must be nonnegative; a supplied period must be positive. Both must be at most 4,294,967,294 milliseconds (about 49.7 days). Null period means one shot; infinite values are rejected. Invalid input preserves the existing schedule.

While scheduled, the neuron delays idle collection, including during long initial delays. Stop and one-shot completion release that protection. Explicit deactivation, migration, or silo failure discards the timer; it never restarts from stored state. Stop suppresses obsolete callbacks but cannot retract signals already in subscriber buffers.

## Reminders

ReminderNeuron owns one native reminder named `tick`. Orleans reminder storage is the only schedule state; there is no duplicate grain state, activation-time re-registration, or per-tick write. The neuron implements `IRemindable` and publishes a ReminderTick when called.

Due time must be nonnegative and fit within the remaining UTC DateTime range. The period must meet `ReminderOptions.MinimumReminderPeriod` (one minute by default). Orleans validates these before updating storage. Reminders are intended for coarse periodic work, not high-frequency timers. Tests explicitly lower this minimum for short integration scenarios.

Registrations reactivate grains and survive restarts when the configured reminder provider is durable. Missed downtime ticks are not replayed. A queued delivery may still publish after Stop or a replacement; no atomic cancellation fence or exact-once delivery is promised. Unknown reminder names are ignored.

Failures reach the caller. A storage/network failure can be ambiguous: registration/removal may have committed before its acknowledgement was lost. Retrying Start replaces the same named registration, and retrying Stop tolerates an already removed registration. The module adds no hidden retry loop.

## Hosting and testing

Call `silo.AddTime()` and configure an Orleans reminder provider. `AddTime()` validates that the provider exists at startup. The in-memory provider is for development/tests and does not survive a process restart.

Use the shared `BrainTestHost`, `ObserveAsync`, `RunBehavior`, and `DeactivateAsync` methods. The Time tests decorate Orleans' ITimerRegistry to arm real timers on demand; callbacks execute on Orleans turns. Controlled timestamps and obsolete-callback injection are explicit test controls, not a virtual Orleans clock. These helpers stay local to Time tests because other modules do not yet need them.

Separate integration scenarios exercise native timers, native reminders, idle collection, explicit deactivation, silo restart, persistent reminders in a fresh host, and the production file app. The restart test waits for an incoming reminder-call recorder before making any grain call, so it verifies autonomous reactivation.

Signals remain live-only and can be lost while a behavior is disconnected. Persistent reminder registration does not make external behavior execution durable. Time contains no countdown snapshots, notes, public generations, durable one-shot alarms, or recovery classifications.

Run `dotnet test --project src/Modules/Time/Tests/DigitalBrain.Modules.Time.Tests.csproj -p:CodeGraphRefresh=false`.

Implementation follows the [Orleans timer and reminder guidance](https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders) and the pinned Orleans 10.3.1 APIs.
