using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Runtime.Http.Surface;

public sealed record ThrottleResult(bool Allowed, DateTimeOffset? RetryAt);

// A per-key hit counter. The host chooses the store: SlidingWindowThrottle in memory, or a neuron for a cluster-wide window.
public interface IRequestThrottle
{
    ValueTask<ThrottleResult> HitAsync(string key, CancellationToken cancellationToken = default);
}

// Counts each call against the caller's network prefix and answers 429 { code, retryAt } once the throttle refuses it.
public sealed class IpThrottleFilter(IRequestThrottle throttle) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var prefix = IpPrefix.Truncate(context.HttpContext.Connection.RemoteIpAddress?.ToString());
        var hit = await throttle.HitAsync(prefix, context.HttpContext.RequestAborted).ConfigureAwait(false);
        return hit.Allowed ? await next(context).ConfigureAwait(false) : Results.Json(new { code = "RATE_LIMITED", retryAt = hit.RetryAt }, statusCode: StatusCodes.Status429TooManyRequests);
    }
}

// A bounded sliding window per key, held in this process only.
public sealed class SlidingWindowThrottle(int limit, TimeSpan window, TimeProvider clock) : IRequestThrottle
{
    private const int MaxKeys = 10_000;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask<ThrottleResult> HitAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (!_hits.TryGetValue(key, out var hits))
            {
                if (_hits.Count >= MaxKeys) { Prune(now); }
                _hits[key] = hits = new Queue<DateTimeOffset>();
            }

            while (hits.Count > 0 && hits.Peek() <= now - window) { hits.Dequeue(); }
            if (hits.Count >= limit) { return ValueTask.FromResult(new ThrottleResult(false, hits.Peek() + window)); }
            hits.Enqueue(now);
            return ValueTask.FromResult(new ThrottleResult(true, null));
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, hits) in _hits.ToArray())
        {
            if (hits.Count == 0 || hits.Last() <= now - window) { _hits.Remove(key); }
        }
    }
}
