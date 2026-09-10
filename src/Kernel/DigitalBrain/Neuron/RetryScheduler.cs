using Orleans.Runtime;

namespace DigitalBrain.Core;

internal sealed class RetryScheduler(IGrainBase grain, Func<CancellationToken, Task> retry, TimeSpan period, Func<bool> hasPendingWork)
{
    internal const string ReminderName = "retry";

    private IGrainTimer? _timer;
    private readonly SemaphoreSlim _reminderGate = new(1, 1);
    private IGrainReminder? _reminder;
    private bool _tickObserved;
    private TimeSpan _delay = TimeSpan.FromSeconds(1);

    internal async Task EnsureReminderAsync()
    {
        await _reminderGate.WaitAsync().ConfigureAwait(true);
        try
        {
            _reminder ??= await grain.RegisterOrUpdateReminder(ReminderName, dueTime: period, period: period).ConfigureAwait(true);
        }
        finally
        {
            _reminderGate.Release();
        }
    }

    internal void ArmTimer()
    {
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

    internal async Task AfterDrainAsync()
    {
        Suspend();
        _delay = TimeSpan.FromSeconds(1);

        await _reminderGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!hasPendingWork() && (_reminder is not null || _tickObserved))
            {
                await RemoveReminderAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            _reminderGate.Release();
        }
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
