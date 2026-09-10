using Orleans.Runtime;

namespace DigitalBrain.Core;

internal sealed class RetryTimer(IGrainBase grain, Func<CancellationToken, Task> retry)
{
    private IGrainTimer? _timer;
    private TimeSpan _delay = TimeSpan.FromSeconds(1);

    internal void Arm()
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

    internal void Disarm()
    {
        _timer?.Dispose();
        _timer = null;
        _delay = TimeSpan.FromSeconds(1);
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
