using System.Diagnostics;

namespace DigitalBrain.Tests.TestSupport;

public static class AsyncTestWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(25);

    public static Task WaitUntilAsync(
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default) =>
        WaitUntilAsync(
            () => Task.FromResult(condition()),
            description,
            timeout,
            pollInterval,
            cancellationToken);

    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string description,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var effectivePollInterval = pollInterval ?? DefaultPollInterval;
        var elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < effectiveTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await condition())
            {
                return;
            }

            var remaining = effectiveTimeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay = remaining < effectivePollInterval ? remaining : effectivePollInterval;
            await Task.Delay(delay, cancellationToken);
        }

        throw new TimeoutException($"Timed out after {effectiveTimeout} waiting for {description}.");
    }
}
