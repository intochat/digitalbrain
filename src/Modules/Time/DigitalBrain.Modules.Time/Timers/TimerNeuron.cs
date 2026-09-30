using DigitalBrain.Core;
using DigitalBrain.Time.Timers.Signals;
using Orleans.Runtime;
namespace DigitalBrain.Time.Timers;

[GrainType("timer")]
internal sealed class TimerNeuron(TimeProvider time) : Neuron, ITimer
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private IGrainTimer? _timer;
    private object? _schedule;

    public Task Start(TimeSpan dueTime, TimeSpan? period = null)
    {
        if (dueTime < TimeSpan.Zero || dueTime > MaximumDelay)
        { throw new ArgumentOutOfRangeException(nameof(dueTime)); }
        if (period is { } interval && (interval <= TimeSpan.Zero || interval > MaximumDelay))
        { throw new ArgumentOutOfRangeException(nameof(period)); }

        var schedule = new object();
        var next = this.RegisterGrainTimer(
            (_, ct) => TickAsync(schedule, period is null, ct),
            0,
            new GrainTimerCreationOptions
            {
                DueTime = dueTime,
                Period = period ?? Timeout.InfiniteTimeSpan,
                Interleave = false,
                KeepAlive = true
            });
        _timer?.Dispose();
        _timer = next;
        _schedule = schedule;
        // KeepAlive extends lifetime on ticks; a long initial delay also needs protection.
        DelayDeactivation(Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        _schedule = null;
        _timer?.Dispose();
        _timer = null;
        DelayDeactivation(TimeSpan.Zero);
        return Task.CompletedTask;
    }

    private async Task TickAsync(object schedule, bool once, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !ReferenceEquals(_schedule, schedule)) { return; }
        if (once) { await Stop(); }
        await PublishAsync(new TimerTick(this.GetPrimaryKeyString(), time.GetUtcNow()));
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _schedule = null;
        _timer?.Dispose();
        _timer = null;
        return base.OnDeactivateAsync(reason, cancellationToken);
    }
}
