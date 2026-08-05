namespace DigitalBrain.Testing;

// The test's grip on time: AdvanceAsync moves the cluster's controllable clock forward and
// drains every timer that came due, in due order, yielding between firings so the turns a
// tick opens can run. Backwards movement is refused by the provider itself.
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The clock lives exactly as long as its composed cluster; the advance gate has no external resources and becomes unreachable with it.")]
public sealed class TestClock
{
    private const int MaximumDrainOperations = 1024;

    private readonly SemaphoreSlim advanceGate = new(1, 1);
    private readonly ControllableTimeProvider provider;

    internal TestClock(ControllableTimeProvider provider) => this.provider = provider;

    public DateTimeOffset UtcNow => provider.GetUtcNow();

    public async Task AdvanceAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        await advanceGate.WaitAsync(cancellationToken);
        try
        {
            var target = provider.GetUtcNow() + duration;
            provider.SetUtcNow(target);

            var operations = 0;
            while (provider.NextDueAtOrBefore(target) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operations >= MaximumDrainOperations)
                {
                    throw new InvalidOperationException(
                        $"Deterministic time drain exceeded {MaximumDrainOperations} operations "
                        + $"while advancing to {target:O}.");
                }

                // Select-then-fire is not atomic: a fired tick's work may dispose the next
                // selection before this attempt — re-poll; the drain bound caps the spins.
                _ = provider.TryFireNextDue(target);
                operations++;
                await Task.Yield();
            }
        }
        finally
        {
            advanceGate.Release();
        }
    }
}
