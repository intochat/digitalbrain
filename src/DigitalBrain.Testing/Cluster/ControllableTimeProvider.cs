using System.Globalization;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

internal sealed class ControllableTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly DateTimeOffset _origin;
    private readonly List<Registration> _registrations = [];

    private DateTimeOffset _utcNow;
    private long _nextSequence;

    internal ControllableTimeProvider(DateTimeOffset origin)
    {
        if (origin.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The controllable time origin must use the UTC offset.",
                nameof(origin));
        }

        _origin = origin;
        _utcNow = origin;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidateTimerValue(dueTime, nameof(dueTime));
        ValidateTimerValue(period, nameof(period));

        lock (_gate)
        {
            var registration = new Registration(
                this,
                callback,
                state,
                _nextSequence++);
            registration.ChangeUnderLock(dueTime, period);
            _registrations.Add(registration);
            return registration;
        }
    }

    internal DateTimeOffset? NextDueAtOrBefore(DateTimeOffset target)
    {
        lock (_gate)
        {
            DateTimeOffset? earliest = null;

            foreach (var registration in _registrations)
            {
                if (registration.NextDue is not { } due || due > target)
                {
                    continue;
                }

                if (earliest is null || due < earliest.Value)
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

        lock (_gate)
        {
            selected = _registrations
                .Where(registration =>
                    registration.NextDue is { } due
                    && due <= now)
                .OrderBy(registration => registration.NextDue)
                .ThenBy(registration => registration.Sequence)
                .FirstOrDefault();

            if (selected is null)
            {
                return false;
            }

            var previousDue = selected.NextDue!.Value;
            selected.NextDue = selected.Period is { } period
                ? Add(previousDue, period, nameof(period))
                : null;
        }

        selected.Invoke();
        return true;
    }

    internal void SetUtcNow(DateTimeOffset value)
    {
        lock (_gate)
        {
            if (value < _utcNow)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Controllable time cannot move backwards within a test method.");
            }

            _utcNow = value;
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            foreach (var registration in _registrations)
            {
                registration.DisposeUnderLock();
            }

            _registrations.Clear();
            _utcNow = _origin;
            _nextSequence = 0;
        }
    }

    internal string DescribePendingAtOrBefore(DateTimeOffset target)
    {
        lock (_gate)
        {
            var descriptions = _registrations
                .Where(registration =>
                    registration.NextDue is { } due
                    && due <= target)
                .OrderBy(registration => registration.NextDue)
                .ThenBy(registration => registration.Sequence)
                .Select(registration => string.Create(
                    CultureInfo.InvariantCulture,
                    $"sequence={registration.Sequence}, due={registration.NextDue:O}, period={registration.Period?.ToString() ?? "disabled"}"))
                .ToArray();

            return descriptions.Length == 0
                ? "none"
                : string.Join("; ", descriptions);
        }
    }

    private static DateTimeOffset Add(
        DateTimeOffset value,
        TimeSpan duration,
        string parameterName)
    {
        try
        {
            return value + duration;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                duration,
                "The timer due instant exceeds the supported DateTimeOffset range.");
        }
    }

    private static void ValidateTimerValue(
        TimeSpan value,
        string parameterName)
    {
        if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Timer values must be non-negative or Timeout.InfiniteTimeSpan.");
        }
    }

    private sealed class Registration(
        ControllableTimeProvider owner,
        TimerCallback callback,
        object? state,
        long sequence) : ITimer
    {
        private bool _disposed;

        internal DateTimeOffset? NextDue { get; set; }

        internal ControllableTimeProvider Owner { get; } = owner;

        internal TimeSpan? Period { get; private set; }

        internal long Sequence { get; } = sequence;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimerValue(dueTime, nameof(dueTime));
            ValidateTimerValue(period, nameof(period));

            lock (Owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                ChangeUnderLock(dueTime, period);
                return true;
            }
        }

        public void Dispose()
        {
            lock (Owner._gate)
            {
                DisposeUnderLock();
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal void ChangeUnderLock(TimeSpan dueTime, TimeSpan period)
        {
            NextDue = dueTime == Timeout.InfiniteTimeSpan
                ? null
                : Add(Owner._utcNow, dueTime, nameof(dueTime));
            Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero
                ? null
                : period;
        }

        internal void DisposeUnderLock()
        {
            _disposed = true;
            NextDue = null;
            Period = null;
        }

        internal void Invoke()
        {
            lock (Owner._gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            callback(state);
        }
    }
}
