using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "TestBrain owns the method-scoped clock; its semaphore becomes unreachable with that scope and has no external resources.")]
public sealed class TestClock
{
    private const int MaximumDrainOperations = 1024;

    private readonly SemaphoreSlim _advanceGate = new(1, 1);
    private readonly ControllableTimeProvider _provider;
    private readonly TestReminderDriver _reminders;
    private bool _disposed;

    internal TestClock(
        ControllableTimeProvider provider,
        TestReminderDriver reminders)
    {
        _provider = provider;
        _reminders = reminders;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed),
                this);
            return _provider.GetUtcNow();
        }
    }

    public async Task AdvanceAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        await _advanceGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var target = Add(_provider.GetUtcNow(), duration);
            var operations = 0;

            while (NextDueAtOrBefore(target) is { } due)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _provider.SetUtcNow(due);

                if (operations >= MaximumDrainOperations)
                {
                    throw DrainLimitFailure(target);
                }

                if (_provider.NextDueAtOrBefore(due) is not null)
                {
                    if (!_provider.TryFireNextDue(due))
                    {
                        throw new InvalidOperationException(
                            "A deterministic timer disappeared before it could be fired.");
                    }

                }
                else if (!await _reminders.TryDeliverNextDueAsync(
                    due,
                    cancellationToken))
                {
                    throw new InvalidOperationException(
                        "A deterministic reminder disappeared before it could be delivered.");
                }

                operations++;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }

            _provider.SetUtcNow(target);
        }
        finally
        {
            _advanceGate.Release();
        }
    }

    internal async ValueTask InvalidateAsync()
    {
        await _advanceGate.WaitAsync();
        try
        {
            _disposed = true;
        }
        finally
        {
            _advanceGate.Release();
        }
    }

    private InvalidOperationException DrainLimitFailure(DateTimeOffset target)
        => new(string.Create(
            CultureInfo.InvariantCulture,
            $"Deterministic time drain exceeded {MaximumDrainOperations} operations while advancing to {target:O}. Pending timers: [{_provider.DescribePendingAtOrBefore(target)}]. Pending reminders: [{_reminders.DescribePendingAtOrBefore(target)}]."));

    private DateTimeOffset? NextDueAtOrBefore(DateTimeOffset target)
    {
        var timer = _provider.NextDueAtOrBefore(target);
        var reminder = _reminders.NextDueAtOrBefore(target);

        if (timer is null)
        {
            return reminder;
        }

        if (reminder is null)
        {
            return timer;
        }

        return timer <= reminder ? timer : reminder;
    }

    private static DateTimeOffset Add(
        DateTimeOffset value,
        TimeSpan duration)
    {
        try
        {
            return value + duration;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                "The requested target exceeds the supported DateTimeOffset range.");
        }
    }
}
