using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Time;

[GrainType("countdown")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from GrainType metadata.")]
internal sealed partial class CountdownNeuron :
    Neuron,
    ICountdown,
    IRemindable
{
    private const string StateName = "time.countdown";
    private const string ReminderPrefix = "time.countdown.";
    private const int MaximumReceipts = 64;
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<CountdownState> _states;

    public CountdownNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<CountdownState>>();
    }

    public Task<CountdownSnapshot> Read()
    {
        var data = LoadIfScheduledBefore();
        return Task.FromResult(
            data is null
                ? new CountdownSnapshot(
                    CountdownStatus.Unscheduled,
                    Generation: 0,
                    Revision: 0,
                    Destination: null,
                    ScheduledAt: null,
                    DueAt: null,
                    Duration: null)
                : Snapshot(data));
    }

    public async Task<CountdownSnapshot> Start(StartCountdown command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        var existing = LoadIfScheduledBefore();
        if (TryReceipt(existing, command.CommandId, out var received))
        {
            return received;
        }

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"Countdown '{Id}' has already been scheduled.");
        }

        ValidateDestination(command.Destination);
        var scheduledAt = TimeProvider.GetUtcNow();
        var dueAt = DueAt(scheduledAt, command.Duration);
        const long generation = 1;
        const long revision = 1;
        var reminderName = ReminderName(generation, revision);
        var data = new CountdownState(
            CountdownStatus.Scheduled,
            generation,
            revision,
            command.Destination,
            scheduledAt,
            dueAt,
            command.Duration,
            [],
            occurrenceCommitted: false,
            reminderName);
        var snapshot = Snapshot(data);
        Remember(data, command.CommandId, snapshot);
        var rollbackState = SerializedState();

        await RegisterReminderAsync(reminderName, command.Duration).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(data, rollbackState).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }

    public async Task<CountdownSnapshot> Reschedule(RescheduleCountdown command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        var current = Load();
        if (TryReceipt(current, command.CommandId, out var received))
        {
            return received;
        }

        RequireScheduled(current);
        RequireRevision(current, command.ExpectedRevision);

        var scheduledAt = TimeProvider.GetUtcNow();
        var dueAt = DueAt(scheduledAt, command.Duration);
        var nextRevision = checked(current.Revision + 1);
        var reminderName = ReminderName(current.Generation, nextRevision);
        var next = Copy(
            current,
            status: CountdownStatus.Scheduled,
            revision: nextRevision,
            scheduledAt: scheduledAt,
            dueAt: dueAt,
            duration: command.Duration,
            occurrenceCommitted: false,
            activeReminderName: reminderName);
        var snapshot = Snapshot(next);
        Remember(next, command.CommandId, snapshot);
        var rollbackState = SerializedState();

        await RegisterReminderAsync(reminderName, command.Duration).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(next, rollbackState).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await RetireReminderAsync(current.ActiveReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }

    public async Task<CountdownSnapshot> Cancel(CancelCountdown command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        var current = Load();
        if (TryReceipt(current, command.CommandId, out var received))
        {
            return received;
        }

        RequireScheduled(current);
        RequireRevision(current, command.ExpectedRevision);

        var next = Copy(
            current,
            status: CountdownStatus.Cancelled,
            revision: current.Revision,
            scheduledAt: current.ScheduledAt,
            dueAt: current.DueAt,
            duration: current.Duration,
            occurrenceCommitted: false,
            activeReminderName: null);
        var snapshot = Snapshot(next);
        Remember(next, command.CommandId, snapshot);

        await SaveAsync(next, SerializedState()).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await RetireReminderAsync(current.ActiveReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }

    public async Task<CountdownSnapshot> Restart(RestartCountdown command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command.CommandId);

        var current = Load();
        if (TryReceipt(current, command.CommandId, out var received))
        {
            return received;
        }

        if (current.Status is not (
            CountdownStatus.Elapsed
            or CountdownStatus.Cancelled))
        {
            throw new InvalidOperationException(
                $"Countdown '{Id}' must be elapsed or cancelled before it can restart.");
        }

        var scheduledAt = TimeProvider.GetUtcNow();
        var dueAt = DueAt(scheduledAt, command.Duration);
        var generation = checked(current.Generation + 1);
        const long revision = 1;
        var reminderName = ReminderName(generation, revision);
        var next = Copy(
            current,
            status: CountdownStatus.Scheduled,
            generation: generation,
            revision: revision,
            scheduledAt: scheduledAt,
            dueAt: dueAt,
            duration: command.Duration,
            occurrenceCommitted: false,
            activeReminderName: reminderName);
        var snapshot = Snapshot(next);
        Remember(next, command.CommandId, snapshot);
        var rollbackState = SerializedState();

        await RegisterReminderAsync(reminderName, command.Duration).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(next, rollbackState).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await RetireReminderAsync(current.ActiveReminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snapshot;
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!TryParseReminderName(reminderName, out var generation, out var revision))
        {
            throw new InvalidOperationException(
                $"Countdown neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        await ElapseIfDue(generation, revision).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
