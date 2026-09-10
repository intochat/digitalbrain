using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Time;

[GrainType("timer")]
internal sealed class TimerNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TimerState> state)
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

            if (State is { Status: TimerStatus.Scheduled })
            {
                throw new CommandRejectedException(arguments.Id, "timer is already scheduled", "Stop the scheduled timer before scheduling another.");
            }

            var generation = (State?.Generation ?? 0) + 1;
            var scheduledAt = TimeProvider.GetUtcNow();
            var dueAt = scheduledAt + TimeSpan.FromSeconds(arguments.DurationSeconds);
            var body = new SchedulingBody(generation, scheduledAt, dueAt, arguments.DurationSeconds, arguments.Note);
            var work = Schedule(Signal.Create(TimeSignals.Scheduling, JsonSerializer.Serialize(body, TimeJson.Default.SchedulingBody)));
            return new Accepted<TimerGeneration>(new TimerGeneration(generation), work);
        });

    public Task<Accepted<TimerGeneration>> Stop(StopTimer command) => ExecuteCommandAsync(
        Descriptor("stop"), command, TimeJson.Default.StopTimer, TimeJson.Default.AcceptedTimerGeneration, arguments =>
        {
            if (State is not { Status: TimerStatus.Scheduled } current)
            {
                throw new CommandRejectedException(arguments.Id, "nothing scheduled to stop", "Schedule a timer before stopping it.");
            }

            var generation = new TimerGeneration(current.Generation);
            var work = Schedule(Signal.Create(TimeSignals.Stopping, JsonSerializer.Serialize(generation, TimeJson.Default.TimerGeneration)));
            return new Accepted<TimerGeneration>(generation, work);
        });

    public Task<TimerSnapshot> Read() => Task.FromResult(State is { } current
        ? new TimerSnapshot(current.Status, current.Generation, current.ScheduledAt, current.DueAt, current.DurationSeconds, current.Note)
        : new TimerSnapshot(TimerStatus.Unscheduled, 0, null, null, null, null));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case TimeSignals.Scheduling:
                {
                    var body = Body(delivery, TimeJson.Default.SchedulingBody);
                    await SaveAsync(new TimerState(TimerStatus.Scheduled, body.Generation, body.ScheduledAt, body.DueAt,
                        body.DurationSeconds, body.Note), cancellationToken).ConfigureAwait(true);
                    await Alarm(body.Generation).Arm(body.DueAt - TimeProvider.GetUtcNow()).ConfigureAwait(true);
                    break;
                }
            case TimeSignals.Stopping:
                {
                    var generation = Body(delivery, TimeJson.Default.TimerGeneration).Value;
                    if (State is { Status: TimerStatus.Scheduled } current && current.Generation == generation)
                    {
                        await SaveAsync(current with { Status = TimerStatus.Cancelled }, cancellationToken).ConfigureAwait(true);
                    }

                    await Alarm(generation).Retire().ConfigureAwait(true);
                    break;
                }
            case TimeSignals.Due:
                {
                    var generation = Body(delivery, TimeJson.Default.TimerGeneration).Value;
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
                    await FireAsync(Signal.Create(TimeSignals.TimerElapsed, JsonSerializer.Serialize(body, TimeJson.Default.TimerElapsedBody)),
                        to: null, delivery.CorrelationId, cancellationToken).ConfigureAwait(true);
                    await Alarm(generation).Retire().ConfigureAwait(true);
                    break;
                }
        }
    }

    private ITimerAlarm Alarm(long generation) => GrainFactory.GetGrain<ITimerAlarm>($"{Id.Name}/{generation}");

    private static T Body<T>(SignalDelivery delivery, JsonTypeInfo<T> json)
        => JsonSerializer.Deserialize(delivery.Signal.Body, json)!;
}
