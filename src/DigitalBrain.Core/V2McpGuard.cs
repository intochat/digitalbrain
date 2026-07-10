using System.Collections.Concurrent;

namespace DigitalBrain.Core.V2;

public sealed record V2McpTransportPolicy(string Audience, IReadOnlySet<string> AllowedOrigins, int MaxBodyBytes, int MaxConcurrentRequests, int RequestsPerMinute);
public sealed class V2McpRequestGuard(V2McpTransportPolicy policy)
{
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _concurrency = new(StringComparer.Ordinal);

    public bool TryBegin(string principal, string? origin, string? audience, int bodyBytes, out IDisposable? lease)
    {
        lease = null;
        if (string.IsNullOrWhiteSpace(principal) || bodyBytes < 0 || bodyBytes > policy.MaxBodyBytes) return false;
        if (!string.Equals(audience, policy.Audience, StringComparison.Ordinal) || (policy.AllowedOrigins.Count > 0 && (origin is null || !policy.AllowedOrigins.Contains(origin)))) return false;
        var now = DateTimeOffset.UtcNow;
        var window = _windows.GetOrAdd(principal, _ => new Window(now));
        lock (window)
        {
            if (now - window.Start >= TimeSpan.FromMinutes(1)) { window.Start = now; window.Count = 0; }
            if (++window.Count > policy.RequestsPerMinute) return false;
        }
        var gate = _concurrency.GetOrAdd(principal, _ => new SemaphoreSlim(policy.MaxConcurrentRequests, policy.MaxConcurrentRequests));
        if (!gate.Wait(0)) return false;
        lease = new Release(gate);
        return true;
    }

    private sealed class Window(DateTimeOffset start) { public DateTimeOffset Start = start; public int Count; }
    private sealed class Release(SemaphoreSlim semaphore) : IDisposable { public void Dispose() => semaphore.Release(); }
}
