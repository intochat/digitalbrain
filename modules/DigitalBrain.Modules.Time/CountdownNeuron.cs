using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Time;

[GrainType("countdown")]
internal sealed class CountdownNeuron :
    Neuron,
    ICountdown,
    ICountdownWakeup,
    IRemindable
{
    private const string StateName = "time.countdown";
    private const string ReminderPrefix = "time.countdown.";
    private const int MaximumReceipts = 64;
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<CountdownState> _states;
    private ITimer? _localTimer;
    private long _localGeneration;
    private long _localRevision;

    public CountdownNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<CountdownState>>();
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

        await RegisterReminderAsync(reminderName, command.Duration);
        await SaveAsync(data, rollbackState);
        ArmLocalTimer(data);

        return snapshot;
    }

    public async Task<CountdownSnapshot> Reschedule(
        RescheduleCountdown command)
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
        var reminderName = ReminderName(
            current.Generation,
            nextRevision);
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

        await RegisterReminderAsync(reminderName, command.Duration);
        await SaveAsync(next, rollbackState);
        await RetireReminderAsync(current.ActiveReminderName);
        ArmLocalTimer(next);

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

        await SaveAsync(next, SerializedState());
        await RetireReminderAsync(current.ActiveReminderName);
        DisposeLocalTimer();

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

        await RegisterReminderAsync(reminderName, command.Duration);
        await SaveAsync(next, rollbackState);
        await RetireReminderAsync(current.ActiveReminderName);
        ArmLocalTimer(next);

        return snapshot;
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

    async Task ICountdownWakeup.Wake(long generation, long revision)
        => await WakeCore(generation, revision);

    private async Task WakeCore(long generation, long revision)
    {
        var reminderName = ReminderName(generation, revision);
        var data = LoadIfScheduledBefore();

        if (data is null
            || data.Status != CountdownStatus.Scheduled
            || data.OccurrenceCommitted
            || data.Generation != generation
            || data.Revision != revision
            || !string.Equals(
                data.ActiveReminderName,
                reminderName,
                StringComparison.Ordinal))
        {
            await RetireReminderAsync(reminderName);
            return;
        }

        var observedAt = TimeProvider.GetUtcNow();

        if (observedAt < data.DueAt)
        {
            ArmLocalTimer(data);
            return;
        }

        var resolution = LocalTimerMatches(generation, revision)
            ? CountdownResolution.OnTime
            : CountdownResolution.Recovered;
        var elapsed = Copy(
            data,
            status: CountdownStatus.Elapsed,
            revision: data.Revision,
            scheduledAt: data.ScheduledAt,
            dueAt: data.DueAt,
            duration: data.Duration,
            occurrenceCommitted: true,
            activeReminderName: null);

        var rollbackState = SerializedState();
        Stage(elapsed);

        try
        {
            await SendAsync(
                data.Destination,
                new CountdownElapsed(
                    Id,
                    data.Generation,
                    data.Revision,
                    data.Destination,
                    data.ScheduledAt,
                    data.DueAt,
                    observedAt,
                    resolution));
        }
        catch
        {
            RestoreState(rollbackState);
            DisposeLocalTimer();
            DeactivateOnIdle();
            throw;
        }

        await RetireReminderAsync(reminderName);
        DisposeLocalTimer();
    }

    async Task IRemindable.ReceiveReminder(
        string reminderName,
        TickStatus status)
    {
        if (!TryParseReminderName(
            reminderName,
            out var generation,
            out var revision))
        {
            throw new InvalidOperationException(
                $"Countdown neuron '{Id}' does not own reminder '{reminderName}'.");
        }

        await WakeCore(generation, revision);
    }

    public override Task OnDeactivateAsync(
        DeactivationReason reason,
        CancellationToken cancellationToken)
    {
        DisposeLocalTimer();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    private CountdownState Load()
        => LoadIfScheduledBefore()
            ?? throw new InvalidOperationException(
                $"Countdown '{Id}' has not been scheduled.");

    private CountdownState? LoadIfScheduledBefore()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : null;

    private void Stage(CountdownState data)
        => _state.Value = _states.SerializeToArray(data);

    private async Task SaveAsync(
        CountdownState data,
        byte[] rollbackState)
    {
        Stage(data);

        try
        {
            await WriteStateAsync();
        }
        catch
        {
            RestoreState(rollbackState);
            throw;
        }
    }

    private byte[] SerializedState()
        => _state.Value is { } serialized
            ? serialized.ToArray()
            : [];

    private void RestoreState(byte[] serialized)
        => _state.Value = serialized;

    private Task<Orleans.Runtime.IGrainReminder> RegisterReminderAsync(
        string reminderName,
        TimeSpan dueTime)
        => this.RegisterOrUpdateReminder(
            reminderName,
            dueTime,
            ReminderPeriod);

    private async Task RetireReminderAsync(string? reminderName)
    {
        if (reminderName is not null
            && await this.GetReminder(reminderName) is { } reminder)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    private void ArmLocalTimer(CountdownState data)
    {
        DisposeLocalTimer();

        _localGeneration = data.Generation;
        _localRevision = data.Revision;
        var self = GrainFactory.GetGrain<ICountdownWakeup>(
            this.GetGrainId());
        var dueTime = data.DueAt - TimeProvider.GetUtcNow();

        if (dueTime < TimeSpan.Zero)
        {
            dueTime = TimeSpan.Zero;
        }

        _localTimer = TimeProvider.CreateTimer(
            _ => self
                .Wake(data.Generation, data.Revision)
                .GetAwaiter()
                .GetResult(),
            state: null,
            dueTime,
            Timeout.InfiniteTimeSpan);
    }

    private void DisposeLocalTimer()
    {
        _localTimer?.Dispose();
        _localTimer = null;
        _localGeneration = 0;
        _localRevision = 0;
    }

    private bool LocalTimerMatches(long generation, long revision)
        => _localTimer is not null
            && _localGeneration == generation
            && _localRevision == revision;

    private void ValidateDestination(NeuronId destination)
    {
        if (destination.Owner != Id.Owner)
        {
            throw new InvalidOperationException(
                $"Destination '{destination}' does not belong to Countdown '{Id}'s owner.");
        }
    }

    private static void Validate(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A command id is required.",
                nameof(commandId));
        }
    }

    private static DateTimeOffset DueAt(
        DateTimeOffset scheduledAt,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "A countdown duration must be positive.");
        }

        try
        {
            return scheduledAt + duration;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "The countdown due instant is outside the supported range.");
        }
    }

    private void RequireScheduled(CountdownState data)
    {
        if (data.Status != CountdownStatus.Scheduled)
        {
            throw new InvalidOperationException(
                $"Countdown '{Id}' is not scheduled.");
        }
    }

    private void RequireRevision(
        CountdownState data,
        long expectedRevision)
    {
        if (data.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                $"Countdown '{Id}' is at revision {data.Revision}, not expected revision {expectedRevision}.");
        }
    }

    private static bool TryReceipt(
        CountdownState? data,
        CommandId commandId,
        out CountdownSnapshot snapshot)
    {
        if (data is not null
            && data.Receipts.TryGetValue(commandId, out var received))
        {
            snapshot = received;
            return true;
        }

        snapshot = null!;
        return false;
    }

    private static void Remember(
        CountdownState data,
        CommandId commandId,
        CountdownSnapshot snapshot)
    {
        while (data.Receipts.Count >= MaximumReceipts)
        {
            data.Receipts.Remove(data.Receipts.First().Key);
        }

        data.Receipts.Add(commandId, snapshot);
    }

    private static CountdownSnapshot Snapshot(CountdownState data)
        => new(
            data.Status,
            data.Generation,
            data.Revision,
            data.Destination,
            data.ScheduledAt,
            data.DueAt,
            data.Duration);

    private static CountdownState Copy(
        CountdownState source,
        CountdownStatus status,
        long? generation = null,
        long? revision = null,
        DateTimeOffset? scheduledAt = null,
        DateTimeOffset? dueAt = null,
        TimeSpan? duration = null,
        bool? occurrenceCommitted = null,
        string? activeReminderName = null)
        => new(
            status,
            generation ?? source.Generation,
            revision ?? source.Revision,
            source.Destination,
            scheduledAt ?? source.ScheduledAt,
            dueAt ?? source.DueAt,
            duration ?? source.Duration,
            new Dictionary<CommandId, CountdownSnapshot>(
                source.Receipts),
            occurrenceCommitted ?? source.OccurrenceCommitted,
            activeReminderName);

    private static string ReminderName(long generation, long revision)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{ReminderPrefix}{generation}.{revision}");

    private static bool TryParseReminderName(
        string reminderName,
        out long generation,
        out long revision)
    {
        generation = 0;
        revision = 0;

        if (!reminderName.StartsWith(
            ReminderPrefix,
            StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = reminderName[ReminderPrefix.Length..];
        var separator = suffix.IndexOf(
            '.',
            StringComparison.Ordinal);

        return separator > 0
            && separator == suffix.LastIndexOf(
                '.')
            && long.TryParse(
                suffix.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out generation)
            && long.TryParse(
                suffix.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out revision)
            && generation > 0
            && revision > 0;
    }
}
