using Orleans.Runtime;

namespace DigitalBrain.Core;

internal sealed class RetryTimer(IGrainBase grain, Func<CancellationToken, Task> retry, TimeSpan period)
{
    private const string ReminderName = "retry";

    private IGrainTimer? _timer;
    private IGrainReminder? _reminder;
    private bool _tickObserved;
    private TimeSpan _delay = TimeSpan.FromSeconds(1);

    internal async Task ArmAsync()
    {
        _reminder ??= await grain.RegisterOrUpdateReminder(ReminderName, dueTime: period, period: period).ConfigureAwait(true);

        if (_timer is not null)
        {
            return;
        }

        _timer = grain.RegisterGrainTimer(RunAsync, new GrainTimerCreationOptions
        {
            DueTime = _delay,
            Period = Timeout.InfiniteTimeSpan,
            Interleave = false,
            KeepAlive = true,
        });
        _delay = TimeSpan.FromSeconds(Math.Min(_delay.TotalSeconds * 2, 60));
    }

    internal void NoteTick() => _tickObserved = true;

    internal async Task DisarmAsync()
    {
        // Work that never failed never registered a reminder, so the hot path costs no lookup.
        if (_reminder is not null || _tickObserved)
        {
            await RemoveReminderAsync().ConfigureAwait(true);
        }

        Suspend();
        _delay = TimeSpan.FromSeconds(1);
    }

    private async Task RemoveReminderAsync()
    {
        _reminder ??= await grain.GetReminder(ReminderName).ConfigureAwait(true);
        if (_reminder is not null)
        {
            try
            {
                await grain.UnregisterReminder(_reminder).ConfigureAwait(true);
            }
            catch (ReminderException)
            {
                // Tolerate a row another activation already removed; anything else is real.
                if (await grain.GetReminder(ReminderName).ConfigureAwait(true) is not null)
                {
                    throw;
                }
            }
        }

        _reminder = null;
        _tickObserved = false;
    }

    internal void Suspend()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var elapsed = _timer;
        _timer = null;
        try
        {
            await retry(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            elapsed?.Dispose();
        }
    }
}
