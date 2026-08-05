namespace DigitalBrain.Testing;

internal sealed class ControllableTimeProvider(DateTimeOffset origin) : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<Registration> registrations = [];

    private DateTimeOffset utcNow = origin.Offset == TimeSpan.Zero
        ? origin
        : throw new ArgumentException("The controllable time origin must use the UTC offset.", nameof(origin));

    private long nextSequence;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return utcNow;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidateTimerValue(dueTime, nameof(dueTime));
        ValidateTimerValue(period, nameof(period));

        lock (gate)
        {
            var registration = new Registration(this, callback, state, nextSequence++);
            registration.ChangeUnderLock(dueTime, period);
            registrations.Add(registration);
            return registration;
        }
    }

    internal DateTimeOffset? NextDueAtOrBefore(DateTimeOffset target)
    {
        lock (gate)
        {
            DateTimeOffset? earliest = null;
            foreach (var registration in registrations)
            {
                if (registration.NextDue is { } due && due <= target && (earliest is null || due < earliest))
                {
                    earliest = due;
                }
            }

            return earliest;
        }
    }

    internal bool TryFireNextDue(DateTimeOffset now)
    {
        Registration? selected;
        lock (gate)
        {
            selected = registrations
                .Where(registration => registration.NextDue is { } due && due <= now)
                .OrderBy(registration => registration.NextDue)
                .ThenBy(registration => registration.Sequence)
                .FirstOrDefault();
            if (selected is null)
            {
                return false;
            }

            var previousDue = selected.NextDue!.Value;
            selected.NextDue = selected.Period is { } period ? previousDue + period : null;
        }

        selected.Invoke();
        return true;
    }

    internal void SetUtcNow(DateTimeOffset value)
    {
        lock (gate)
        {
            if (value < utcNow)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "Controllable time refuses to run backwards.");
            }

            utcNow = value;
        }
    }

    private static void ValidateTimerValue(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, value, "Timer values must be non-negative or Timeout.InfiniteTimeSpan.");
        }
    }

    private sealed class Registration(
        ControllableTimeProvider owner, TimerCallback callback, object? state, long sequence) : ITimer
    {
        private bool disposed;

        internal DateTimeOffset? NextDue { get; set; }

        internal TimeSpan? Period { get; private set; }

        internal long Sequence { get; } = sequence;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimerValue(dueTime, nameof(dueTime));
            ValidateTimerValue(period, nameof(period));

            lock (owner.gate)
            {
                if (disposed)
                {
                    return false;
                }

                ChangeUnderLock(dueTime, period);
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                disposed = true;
                NextDue = null;
                Period = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal void ChangeUnderLock(TimeSpan dueTime, TimeSpan period)
        {
            NextDue = dueTime == Timeout.InfiniteTimeSpan ? null : owner.utcNow + dueTime;
            Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period;
        }

        internal void Invoke()
        {
            lock (owner.gate)
            {
                if (disposed)
                {
                    return;
                }
            }

            callback(state);
        }
    }
}
