# Time primitives implementation plan

> Execute inline using the executing-plans workflow. The user approved the design; proceed without another design gate.

**Goal:** Replace the countdown module with Orleans-backed timer and reminder primitives.

**Architecture:** ITimer and IReminder extend INeuron and publish separate typed ticks. One schedule per grain identity; timers belong to activations and reminder registrations belong to Orleans storage. BrainTestHost remains the reusable simulation surface.

**Tech stack:** C#, .NET 11, Orleans 10.3.1, xUnit, InProcessTestCluster.

**Spec:** `docs/migration/time-module-plan.md` (approved).

## Constraints and review focus

- Preserve live-only signals, the existing observer lifetime guarantees, and the user's unrelated Neuron edits.
- No compatibility shims, duplicate reminder state, timer snapshots, application notes, or durable one-shot alarms.
- Invalid replacement must leave the original schedule intact, including out-of-range TimeSpan values.
- Stop and replacement must not revive a disposed timer. Queued reminder delivery must have explicit semantics.
- Restart tests must not activate a reminder by polling it.
- Tests must execute callbacks on Orleans turns; changing timestamps alone is not scheduler simulation.
- Missing configuration and provider failures must be surfaced.

## Task 1: timer primitive and controlled delivery

Files: Time/Contracts/ITimer.cs and TimerTick.cs; Time/Time/TimerNeuron.cs; Time/Tests/TimerFacts.cs and TimerTestSupport.cs.

- [x] Verify the pinned ITimerRegistry and IGrainTimer interfaces. Use a test decorator that arms real grain timers on demand, so callbacks still run on Orleans turns.
- [x] Replace countdown tests with scenarios for one-shot delivery, repetition, replacement, idempotent Stop, validation preserving active registration, and loss on activation restart. Run tests and observe missing-contract failures.
- [x] Implement `Task Start(TimeSpan dueTime, TimeSpan? period = null)` and `Task Stop()`. Validate finite nonnegative due time and finite positive periods before registration. KeepAlive=true, Interleave=false. Create replacement before disposing old handle; clear/dispose one-shot handle before publishing.
- [x] Run the Time tests after migrating all consumers in Task 3; prove actual timer delivery separately from controlled firing.

Example public scenario:

```csharp
var timer = host.Brain.Get<ITimer>("tea");
await using var ticks = await host.ObserveAsync<TimerTick>(timer, ct);
await timer.Start(TimeSpan.Zero);
Assert.Equal("tea", (await ticks.NextAsync(ct: ct)).TimerId);
await timer.Stop();
await timer.Stop();
```

## Task 2: reminder primitive

Files: Time/Contracts/IReminder.cs and ReminderTick.cs; Time/Time/ReminderNeuron.cs; Time/Tests/ReminderFacts.cs and ReminderTestSupport.cs.

- [x] Write tests using the real reminder provider: Start updates a single named registration, invalid Start preserves registration, Stop removes it and repeats safely, provider failures propagate, registration survives restart, and reminder delivery autonomously activates the grain. Observe missing-contract failures.
- [x] Implement `Task Start(TimeSpan dueTime, TimeSpan period)` and `Task Stop()` with IReminderRegistry and one stable name. Validate before registration using the configured minimum period. Implement IRemindable to publish ReminderTick. Do not add state storage or activation re-registration.
- [x] Use a test-only incoming call filter to record reminder deliveries after invoking the actual handler. The restart test waits on that recorder before making any grain call.
- [x] Document queued reminder delivery as allowed after Stop/update and test stale/unknown names without promising stronger distributed guarantees.

```csharp
var reminder = host.Brain.Get<IReminder>("daily");
await reminder.Start(TimeSpan.Zero, TimeSpan.FromMinutes(1));
await reminder.Stop();
await reminder.Stop();
```

## Task 3: migrate consumers and delete countdown machinery

Files: Behaviors/TimerReport.cs and timer-report.cs; Time/Tests/TimerBehaviorFacts.cs and TimerProcessFacts.cs; Testing/README.md; Time/README.md; migration status docs.

- [x] Convert behavior and separate-process smoke test to TimerTick and Start. Preserve subscribe-before-trigger and clean child-process shutdown.
- [x] Delete TimerState, TimerSnapshot, TimerStatus, TimerResolution, TimerElapsed, old countdown failure tests and test-only subclass.
- [x] Document lifetime, parameter limits, repeat semantics, signal loss, provider configuration, and controlled-vs-real testing.
- [x] Run `dotnet test --solution DigitalBrain.Foundation.slnx -c Release -p:CodeGraphRefresh=false`. Expected: all runtime and Time scenarios pass, including the separate production process.
- [x] Review the implementation for lifecycle races and provider assumptions; use one independent reviewer after verification. Leave changes reviewable without committing unrelated edits.

## Execution record

- Design approved. Existing working tree has unrelated null-check removals in Neuron.cs; preserve them.
- Context7 quota exhausted; use pinned package XML and official Orleans sources/documentation for API evidence.
- Tasks 1–3 complete. Missing-contract tests first failed with absent Start/TimerTick/IReminder; lifecycle tests first failed with missing BrainTestHost.DeactivateAsync. Final Release foundation suite: 49 passed (27 runtime, 22 Time), no failures/skips, 7.4 seconds. The Time-only suite also passed during development.
- Ruling: use the existing native ITimerRegistry seam instead of adding a production scheduling abstraction. Controlled firing arms native grain timers on their scheduler; obsolete callbacks are explicitly injected on the scheduler. Cost: these are controlled delivery scenarios, not a virtual-clock simulation of Orleans.
- Ruling: KeepAlive alone does not cover infrequent timers in the pinned API. DelayDeactivation indefinitely while scheduled, release on Stop/completion. Cost: started periodic timers retain activations until stopped or explicitly deactivated; documented for consumers and tested against real idle collection.
- Ruling: reminder parameter validation delegates to the native registry/service. Too-small periods use the native ArgumentException contract. Cost: supported ranges and exception details follow the pinned Orleans behavior; integration tests verify validation-before-write and long-period support.
- Independent review found no confirmed production correctness defect. Its significant test gaps were filled: obsolete callback injection, provider errors before/after commit and lookup failure, queued reminder delivery/unknown names, real idle collection, duration boundaries, and timer loss on restart.
- Minor review note: callback nonoverlap is inherited from the real noninterleaving Orleans timer and its completion-relative cadence; tests assert the noninterleaving option and real repeated delivery, but do not independently hold a callback open to stress overlap. No exact timing guarantee is claimed.
- No changes committed during this implementation. The unrelated Neuron edits remain untouched.
