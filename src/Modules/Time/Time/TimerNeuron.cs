using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Time;

[GrainType("timer")]
internal sealed class TimerNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TimerState>> state)
    : Neuron<TimerState>(runtime, state), ITimer
{
    private const int RecoveredAfterMinutes = 1;

    public Task<Accepted<TimerGeneration>> Schedule(ScheduleTimer command) => ExecuteCommandAsync(
        Descriptor("schedule"), command, TimeJson.Default.ScheduleTimer, TimeJson.Default.AcceptedTimerGeneration, arguments =>
        {
            if (arguments.DurationSeconds <= 0)
            {
                throw new CommandRejectedException(arguments.Id, "duration must be positive", "Provide a duration greater than zero seconds.");
            }

            if (string.IsNullOrWhiteSpace(arguments.Note))
            {
                throw new CommandRejectedException(arguments.Id, "note is blank", "Provide a non-blank note for the timer.");
            }

            if (arguments.ExpectedVersion is { } expected && expected != (State?.Generation ?? 0))
            {
                throw new CommandRejectedException(arguments.Id, $"expected generation {expected} but the timer is at {State?.Generation ?? 0}",
                    "Read the timer and retry with the generation it reports, or stop it first.");
            }

            var body = new SchedulingBody(arguments.DurationSeconds, arguments.Note);
            var work = Schedule(Signal.FromJson(TimeSignals.TimerScheduling, body, TimeJson.Default.SchedulingBody));
            return new Accepted<TimerGeneration>(new TimerGeneration(State?.Generation ?? 0), work);
        });

    public Task<Accepted<TimerGeneration>> Stop(StopTimer command) => ExecuteCommandAsync(
        Descriptor("stop"), command, TimeJson.Default.StopTimer, TimeJson.Default.AcceptedTimerGeneration, arguments =>
        {
            if (State is not { Status: TimerStatus.Scheduled } current)
            {
                throw new CommandRejectedException(arguments.Id, "nothing scheduled to stop", "Schedule a timer before stopping it.");
            }

            var work = Schedule(Signal.Create(TimeSignals.TimerStopping, "{}"));
            return new Accepted<TimerGeneration>(new TimerGeneration(current.Generation), work);
        });

    public Task<TimerSnapshot> Read() => Task.FromResult(State is { } current
        ? new TimerSnapshot(current.Status, current.Generation, current.ScheduledAt, current.DueAt, current.DurationSeconds, current.Note)
        : new TimerSnapshot(TimerStatus.Unscheduled, 0, null, null, null, null));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case TimeSignals.TimerScheduling:
                {
                    if (State is { Status: TimerStatus.Scheduled })
                    {
                        // A racing schedule is a no-op while the timer is already armed.
                        return;
                    }

                    if (Body(delivery, TimeJson.Default.SchedulingBody) is not { } body)
                    {
                        return;
                    }

                    var generation = (State?.Generation ?? 0) + 1;
                    var scheduledAt = TimeProvider.GetUtcNow();
                    var dueAt = scheduledAt + TimeSpan.FromSeconds(body.DurationSeconds);
                    await SaveAsync(new TimerState(TimerStatus.Scheduled, generation, scheduledAt, dueAt,
                        body.DurationSeconds, body.Note), cancellationToken).ConfigureAwait(true);
                    await Alarm(generation).Arm(dueAt - TimeProvider.GetUtcNow()).ConfigureAwait(true);
                    break;
                }
            case TimeSignals.TimerStopping:
                {
                    if (State is not { Status: TimerStatus.Scheduled } current)
                    {
                        return;
                    }

                    await SaveAsync(current with { Status = TimerStatus.Cancelled }, cancellationToken).ConfigureAwait(true);
                    await Alarm(current.Generation).Retire().ConfigureAwait(true);
                    break;
                }
            case TimeSignals.TimerDue:
                {
                    if (Body(delivery, TimeJson.Default.TimerGeneration) is not { } generationBody)
                    {
                        return;
                    }

                    var generation = generationBody.Value;
                    if (State is not { Status: TimerStatus.Scheduled } current || current.Generation != generation)
                    {
                        await Alarm(generation).Retire().ConfigureAwait(true);
                        return;
                    }

                    var observedAt = TimeProvider.GetUtcNow();
                    if (observedAt < current.DueAt)
                    {
                        await Alarm(generation).Arm(current.DueAt - observedAt).ConfigureAwait(true);
                        return;
                    }

                    var resolution = observedAt > current.DueAt.AddMinutes(RecoveredAfterMinutes)
                        ? TimerResolution.Recovered
                        : TimerResolution.OnTime;
                    await SaveAsync(current with { Status = TimerStatus.Elapsed }, cancellationToken).ConfigureAwait(true);
                    var body = new TimerElapsedBody(Id, generation, current.ScheduledAt, current.DueAt, observedAt, resolution, current.Note);
                    await FireAsync(Signal.FromJson(TimeSignals.TimerElapsed, body, TimeJson.Default.TimerElapsedBody),
                        to: null, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
                    await Alarm(generation).Retire().ConfigureAwait(true);
                    break;
                }
        }
    }

    private ITimerAlarm Alarm(long generation) => GrainFactory.GetGrain<ITimerAlarm>($"{Id.Name}/{generation}");
}
