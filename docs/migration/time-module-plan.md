# Time module: proposed design and migration plan

Status: approved and implemented; see `../superpowers/plans/2026-09-19-time-primitives.md` for execution notes and `../../src/Modules/Time/README.md` for the resulting contract.

## Intent

Encapsulate Orleans timers and reminders behind small DigitalBrain contracts which C# file-based behaviors and other modules can use. Keep scheduling mechanics in Time, application decisions in behaviors, and verify observable behavior through the shared Brain testing host.

Retain the agreed platform guarantees: persisted module state where needed, live signals without replay, and no compatibility requirement for old contracts or stored state. A durable reminder registration does not make delivery to an external behavior durable.

## Recommendation and alternatives

Use separate `ITimer` and `IReminder` contracts, each implemented by a neuron. Their different lifetimes should be visible in their names and documentation.

Alternatives considered:

- One scheduler with a durability flag: fewer types, but substantially more conditional behavior and invalid combinations for consumers to learn.
- Keep the existing durable countdown and add a transient timer: useful if countdowns are the actual product requirement, but preserves application policy inside the infrastructure module.

Recommended starting scope is scheduling primitives: one named schedule per grain identity, one-shot or periodic timers, and periodic reminders. Durable one-shot alarms are a separate capability, only added if a concrete behavior needs them.

## Proposed contracts

```csharp
public interface ITimer : INeuron
{
    Task Start(TimeSpan dueTime, TimeSpan? period = null);
    Task Stop();
}

public interface IReminder : INeuron
{
    Task Start(TimeSpan dueTime, TimeSpan period);
    Task Stop();
}
```

`Start` replaces the existing schedule for that identity; `Stop` is idempotent. Validate parameters before replacing anything. Timer due time is nonnegative; a supplied period is positive. Reminder periods must satisfy the configured Orleans minimum, with a documented production default of at least one minute. A null timer period means one shot. Do not expose Orleans handles, callbacks, TickStatus, or configuration flags in contracts.

Publish separate `TimerTick` and `ReminderTick` signals containing source identity and observed UTC time. Include scheduled time only where it has an honest, defined meaning; a timer is completion-relative, not an exact wall-clock schedule. Do not publish an inferred `Recovered` status. No per-tick persistence, replay, sequence history, notes, expected-generation arguments, or public state-machine snapshots in the initial contract.

Consumers subscribe before starting when they need the first tick. Returning from Start means registration completed, not that a consumer has received a tick. Stop prevents future scheduling; already delivered or queued live signals are not recalled.

## Implementation

`TimerNeuron` owns one `IGrainTimer` and uses `RegisterGrainTimer`, with noninterleaving callbacks. Use KeepAlive while scheduled so idle collection does not silently terminate an explicitly started timer. Dispose on Stop/replacement; release the handle after a one-shot tick. Silo failure or explicit deactivation loses the schedule. A fresh activation starts stopped. Use a private schedule token if needed to reject obsolete queued callbacks after replacement; do not expose it as a public generation protocol.

`ReminderNeuron` implements `IRemindable` and uses one stable reminder name per grain. Orleans reminder storage is the source of truth for its periodic registration. Avoid duplicating the registration in grain state or re-registering on every activation. Start awaits RegisterOrUpdateReminder; Stop looks up and unregisters the reminder. Propagate registration/removal failures. The implementation must characterize queued delivery around Stop/update before promising suppression; do not invent stronger guarantees than the provider offers.

The reminder must reactivate its grain without a consumer polling or calling Read. Missed ticks during downtime are not replayed. Signals produced while there is no live subscriber may be lost, as agreed for the framework.

Keep Time-specific scheduling internals out of Neuron and BrainClient. Use TimeProvider for timestamps, not as a claim that Orleans scheduling has become virtual. Keep hosting configuration small and fail clearly if reminder capability is enabled without a provider.

## Testing design

Use BrainTestHost, Brain.Get, ObserveAsync, RunBehavior, persisted test stores, and restart facilities as the public test surface. Do not create a second fake brain or expose callback-driving methods in production contracts.

Two complementary layers:

1. Deterministic module scenarios: if virtual scheduling is required, introduce one internal scheduling seam with a production Orleans adapter and a manual test adapter. The manual adapter must deliver callbacks through Orleans grain turns, await their completion, and model cancellation/replacement. It must not call grain objects directly from the test thread. Keep Time-specific helpers with Time tests initially; promote helpers to DigitalBrain.Testing only when another module actually needs them. Explicitly label these as controlled delivery tests.
2. Real Orleans integration scenarios: verify actual timer callbacks, actual reminder registration and delivery, grain reactivation, and restart persistence. Use bounded asynchronous waits and signal probes, not fixed sleeps. These tests establish the guarantees that manual delivery cannot prove.

Do not add the virtual scheduler before a small feasibility check establishes the Orleans test hooks and callback dispatch mechanism. The current ManualClock only changes GetUtcNow, and the current test grain invokes OnDueAsync manually; neither advances the Orleans scheduler.

Required scenarios:

| Capability | Observable verification |
| --- | --- |
| One-shot timer | No early tick under controlled delivery; one tick; no further scheduling |
| Periodic timer | Multiple ticks; callback executions do not overlap |
| Replacement | New schedule replaces previous schedule; stale callback cannot publish |
| Stop | Repeated Stop succeeds; no newly scheduled ticks; buffered signals are explicitly outside this guarantee |
| Validation | Invalid Start leaves an existing schedule intact |
| Activation lifetime | Timer disappears after deactivation/restart and must be started again |
| Reminder lifetime | Registration survives grain deactivation and persisted-host restart |
| Autonomous wakeup | Reminder causes activation after restart without any grain call from the test; use a test-only activation/delivery recorder |
| Reminder failures | Registration/removal failures reach caller; retry behavior matches documented provider semantics |
| Live delivery | Disconnected behavior receives no replay; reconnect and observe subsequent ticks |
| File-based behavior | A production file app uses only contracts and Brain subscription methods |

Avoid exact millisecond assertions against real scheduling. A test that repeatedly reads the reminder grain after restart cannot prove autonomous reminder reactivation.

## Implementation order

1. Agree whether Time exposes primitives as proposed or must also retain durable one-shot countdowns. Confirm replacement semantics and absence of status/history requirements.
2. Characterize Orleans timer disposal/replacement, reminder Stop/update races, minimum period configuration, and available deterministic testing hooks. This is a bounded investigation, not a new scheduling framework.
3. Implement transient ITimer and TimerNeuron with contract-level scenarios using BrainTestHost. Add a real timer delivery test immediately.
4. Implement IReminder and ReminderNeuron using native registration persistence. Add restart and autonomous activation tests with the existing file reminder store.
5. Add only the controlled scheduling helper justified by scenarios that would otherwise be slow or nondeterministic. Run matching real-adapter tests for its key assumptions.
6. Migrate the Time sample/file app and tests. Delete the old countdown state machine, TimerState, TimerSnapshot, TimerStatus, TimerResolution, note payload, generation preconditions, and test subclass once references are migrated. Update foundation documentation.
7. Run the Time suite, the foundation suite in Release, and the separate production file-app scenario. Review lifecycle races and check contract/project dependencies.

Completion means consumers never use Orleans timer/reminder APIs directly, lifetimes are explicit, no duplicate persistence is maintained, and both controlled scenarios and actual Orleans execution substantiate the documented guarantees.

## Documentation evidence

Microsoft documents activation-scoped timers, completion-relative periods, noninterleaving callbacks, KeepAlive, persistent reminder definitions, autonomous activation, and missed downtime ticks: https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders

Context7 lookup was attempted but its monthly quota was exhausted; official Microsoft documentation was used instead.
