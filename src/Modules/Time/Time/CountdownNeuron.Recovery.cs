using System.Globalization;

namespace DigitalBrain.Time;

internal sealed partial class CountdownNeuron
{
    private async Task ElapseIfDue(long generation, long revision)
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
            await RetireReminderAsync(reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var observedAt = TimeProvider.GetUtcNow();
        if (observedAt < data.DueAt)
        {
            await RegisterReminderAsync(reminderName, data.DueAt - observedAt).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        var resolution = observedAt > data.DueAt + ReminderPeriod
            ? CountdownResolution.Recovered
            : CountdownResolution.OnTime;
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
                    resolution)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            RestoreState(rollbackState);
            DeactivateOnIdle();
            throw;
        }

        await RetireReminderAsync(reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private Task<Orleans.Runtime.IGrainReminder> RegisterReminderAsync(string reminderName, TimeSpan dueTime)
        => this.RegisterOrUpdateReminder(reminderName, dueTime, ReminderPeriod);

    private async Task RetireReminderAsync(string? reminderName)
    {
        if (reminderName is not null
            && await this.GetReminder(reminderName).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext) is { } reminder)
        {
            await this.UnregisterReminder(reminder).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private static string ReminderName(long generation, long revision)
        => string.Create(CultureInfo.InvariantCulture, $"{ReminderPrefix}{generation}.{revision}");

    private static bool TryParseReminderName(string reminderName, out long generation, out long revision)
    {
        generation = 0;
        revision = 0;

        if (!reminderName.StartsWith(ReminderPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = reminderName[ReminderPrefix.Length..];
        var separator = suffix.IndexOf('.', StringComparison.Ordinal);

        return separator > 0
            && separator == suffix.LastIndexOf('.', StringComparison.Ordinal)
            && long.TryParse(suffix.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out generation)
            && long.TryParse(suffix.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out revision)
            && generation > 0
            && revision > 0;
    }
}
