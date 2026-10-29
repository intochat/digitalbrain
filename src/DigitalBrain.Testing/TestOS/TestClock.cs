namespace DigitalBrain.Testing;

/// <summary>
/// The TestOS clock: time moves only through <see cref="Advance(TimeSpan)"/>, which fires due timers in order. Neuron timers,
/// reminders and call timeouts all run on it; their ticks are queued as turns, so await <see cref="TestOS.Idle"/> after advancing.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ClockTimer> _timers = [];
    private DateTimeOffset _now;
    private long _order;

    /// <summary>Starts at <paramref name="start"/>, or 2026-01-01 UTC.</summary>
    public TestClock(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) { return _now; }
    }

    /// <summary>Moves the clock forward by <paramref name="by"/>, firing every timer that falls due, in due order.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="by"/> is negative.</exception>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        DateTimeOffset end;
        lock (_gate) { end = _now + by; }
        while (true)
        {
            ClockTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => timer.Due <= end).OrderBy(timer => timer.Due).ThenBy(timer => timer.Order).FirstOrDefault();
                if (next is null) { _now = end; return; }
                if (next.Due > _now) { _now = next.Due; }
                if (next.Period is { } period) { next.Due += period; }
                else { _timers.Remove(next); }
            }
            next.Fire();
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ClockTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ClockTimer(TestClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; set; }

        public TimeSpan? Period { get; private set; }

        public long Order { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan) { return true; }
                Due = clock._now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
                Period = period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero ? null : period;
                Order = ++clock._order;
                clock._timers.Add(this);
            }
            return true;
        }

        public void Dispose()
        {
            lock (clock._gate) { clock._timers.Remove(this); }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
