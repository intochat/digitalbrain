using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

internal sealed partial class CountdownNeuron
{
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

    private async Task SaveAsync(CountdownState data, byte[] rollbackState)
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

    private static bool TryReceipt(CountdownState? data, CommandId commandId, out CountdownSnapshot snapshot)
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

    private static void Remember(CountdownState data, CommandId commandId, CountdownSnapshot snapshot)
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
            new Dictionary<CommandId, CountdownSnapshot>(source.Receipts),
            occurrenceCommitted ?? source.OccurrenceCommitted,
            activeReminderName);

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
            throw new ArgumentException("A command id is required.", nameof(commandId));
        }
    }

    private static DateTimeOffset DueAt(DateTimeOffset scheduledAt, TimeSpan duration)
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

    private void RequireRevision(CountdownState data, long expectedRevision)
    {
        if (data.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                $"Countdown '{Id}' is at revision {data.Revision}, not expected revision {expectedRevision}.");
        }
    }
}
