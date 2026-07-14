using Orleans;
namespace DigitalBrain.Mcp;

public sealed class BoundedOrleansClientConnectionRetryFilter(ILogger<BoundedOrleansClientConnectionRetryFilter> logger) : IClientConnectionRetryFilter
{
    private const int MaximumAttempts = 60;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private int _attempts;
    public async Task<bool> ShouldRetryConnectionAttempt(Exception exception, CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _attempts);
        if (attempt > MaximumAttempts) return false;
        logger.LogWarning(
            "Orleans gateway is not ready; retrying client connection ({Attempt}/{MaximumAttempts}, {ExceptionType}).",
            attempt,
            MaximumAttempts,
            exception.GetType().Name);
        await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
