using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "TestBrain owns the method-scoped clock; its semaphore becomes unreachable with that scope and has no external resources.")]
public sealed class TestClock
{
    private const int MaximumDrainOperations = 1024;

    private readonly SemaphoreSlim _advanceGate = new(1, 1);
    private readonly BrainTestDiagnostics _diagnostics;
    private readonly ControllableTimeProvider _provider;
    private readonly TestReminderDriver _reminders;
    private bool _disposed;

    internal TestClock(ControllableTimeProvider provider, TestReminderDriver reminders, BrainTestDiagnostics diagnostics)
    {
        _provider = provider;
        _reminders = reminders;
        _diagnostics = diagnostics;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
            return _provider.GetUtcNow();
        }
    }

    public async Task AdvanceAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
            await AdvanceCoreAsync(duration, cancellationToken);
        }
        catch (Exception failure)
            when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure("clock.advance", failure);
        }
    }

    private async Task AdvanceCoreAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        await _advanceGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var target = Add(_provider.GetUtcNow(), duration);
            var operations = 0;

            _provider.SetUtcNow(target);

            while (TrySelectNextDue(target, out var nextIsTimer))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (operations >= MaximumDrainOperations)
                {
                    throw DrainLimitFailure(target);
                }

                // Select then fire/deliver is not atomic: a prior due item's grain work may Disarm or
                // Dispose the next selection before this attempt. Re-poll; the drain bound still caps spins.
                var delivered = nextIsTimer
                    ? _provider.TryFireNextDue(target)
                    : await _reminders.TryDeliverNextDueAsync(target, cancellationToken);

                operations++;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                if (!delivered)
                {
                    continue;
                }
            }
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

    private static InvalidOperationException DrainLimitFailure(DateTimeOffset target)
        => new(
            $"Deterministic time drain exceeded {MaximumDrainOperations} operations while advancing to {target:O}.");

    private bool TrySelectNextDue(DateTimeOffset target, out bool nextIsTimer)
    {
        var timer = _provider.NextDueAtOrBefore(target);
        var reminder = _reminders.NextDueAtOrBefore(target);

        if (timer is null && reminder is null)
        {
            nextIsTimer = false;
            return false;
        }

        // Equal due instants favor timers, preserving one stable cross-source tie rule.
        nextIsTimer = timer is not null
            && (reminder is null || timer <= reminder);
        return true;
    }

    private static DateTimeOffset Add(DateTimeOffset value, TimeSpan duration)
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
