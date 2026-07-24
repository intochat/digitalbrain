using Orleans.Timers;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

internal sealed class TestReminderRegistry(
    VolatileReminderTable table,
    ControllableTimeProvider clock) : IReminderRegistry
{
    private static readonly TimeSpan MinimumPeriod =
        TimeSpan.FromMilliseconds(50);

    public async Task<IGrainReminder> RegisterOrUpdateReminder(
        GrainId callingGrainId,
        string reminderName,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ValidateName(reminderName);
        ValidateDueTime(dueTime);
        ValidatePeriod(reminderName, period);

        DateTime startAt;
        try
        {
            startAt = (clock.GetUtcNow() + dueTime).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dueTime),
                dueTime,
                "The reminder due instant exceeds the supported DateTimeOffset range.");
        }

        var entry = new ReminderEntry
        {
            GrainId = callingGrainId,
            ReminderName = reminderName,
            StartAt = startAt,
            Period = period,
        };
        var etag = await table.UpsertRow(entry);

        return new ReminderHandle(callingGrainId, reminderName, etag);
    }

    public async Task UnregisterReminder(
        GrainId callingGrainId,
        IGrainReminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        if (reminder is not ReminderHandle handle
            || handle.GrainId != callingGrainId)
        {
            throw new ArgumentException(
                "The reminder handle does not belong to the calling grain.",
                nameof(reminder));
        }

        var removal = table.RemoveRowWithStatus(
            callingGrainId,
            handle.ReminderName,
            handle.ETag);

        if (removal == ReminderRemovalResult.ETagMismatch)
        {
            throw new ReminderException(
                $"Cannot unregister reminder '{handle.ReminderName}' for grain '{callingGrainId}' because its ETag no longer matches the registered reminder.");
        }

        await Task.CompletedTask;
    }

    public async Task<IGrainReminder> GetReminder(
        GrainId callingGrainId,
        string reminderName)
    {
        ValidateName(reminderName);

        var entry = await table.ReadRow(callingGrainId, reminderName);
        return entry is null
            ? null!
            : new ReminderHandle(
                entry.GrainId,
                entry.ReminderName,
                entry.ETag);
    }

    public async Task<List<IGrainReminder>> GetReminders(
        GrainId callingGrainId)
    {
        var data = await table.ReadRows(callingGrainId);
        return
        [
            .. data.Reminders.Select(entry =>
                (IGrainReminder)new ReminderHandle(
                    entry.GrainId,
                    entry.ReminderName,
                    entry.ETag)),
        ];
    }

    private static void ValidateName(string reminderName)
    {
        if (string.IsNullOrEmpty(reminderName))
        {
            throw new ArgumentException(
                "Cannot use a null or empty reminder name.",
                nameof(reminderName));
        }
    }

    private static void ValidateDueTime(TimeSpan dueTime)
    {
        if (dueTime == Timeout.InfiniteTimeSpan || dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dueTime),
                dueTime,
                "Reminder due time must be non-negative and finite.");
        }
    }

    private static void ValidatePeriod(
        string reminderName,
        TimeSpan period)
    {
        if (period == Timeout.InfiniteTimeSpan || period < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(period),
                period,
                "Reminder period must be non-negative and finite.");
        }

        if (period < MinimumPeriod)
        {
            throw new ArgumentException(
                $"Cannot register reminder '{reminderName}' because period {period} is less than the minimum {MinimumPeriod}.",
                nameof(period));
        }
    }

    private sealed record ReminderHandle(
        GrainId GrainId,
        string ReminderName,
        string ETag) : IGrainReminder;
}
