using System.Collections.Concurrent;

namespace DigitalBrain.Kernel.Contracts.Runtime;

public sealed record McpTransportPolicy(string Audience, IReadOnlySet<string> AllowedOrigins, int MaxBodyBytes, int MaxConcurrentRequests, int RequestsPerMinute);
public sealed class McpRequestGuard
{
    private const int MaximumTrackedPrincipals = 4096;
    private static readonly TimeSpan IdleRetention = TimeSpan.FromMinutes(2);
    private readonly McpTransportPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, PrincipalWindow> _principals = new(StringComparer.Ordinal);
    private int _cleanupCounter;

    public McpRequestGuard(McpTransportPolicy policy, TimeProvider? timeProvider = null)
    {
        if (policy.MaxBodyBytes < 1 || policy.MaxConcurrentRequests < 1 || policy.RequestsPerMinute < 1)
            throw new ArgumentException("MCP transport limits must be positive.", nameof(policy));
        _policy = policy;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int MaxBodyBytes => _policy.MaxBodyBytes;

    public bool TryBegin(string principal, string? origin, string? audience, long? bodyBytes, out IDisposable? lease)
    {
        lease = null;
        if (string.IsNullOrWhiteSpace(principal) || bodyBytes is < 0 || bodyBytes > _policy.MaxBodyBytes) return false;
        if (!string.Equals(audience, _policy.Audience, StringComparison.Ordinal) ||
            (_policy.AllowedOrigins.Count > 0 && (origin is null || !_policy.AllowedOrigins.Contains(origin)))) return false;
        var now = _timeProvider.GetUtcNow();
        if ((Interlocked.Increment(ref _cleanupCounter) & 255) == 0 || _principals.Count >= MaximumTrackedPrincipals)
            RemoveIdle(now);
        if (!_principals.TryGetValue(principal, out var window))
        {
            if (_principals.Count >= MaximumTrackedPrincipals) return false;
            window = _principals.GetOrAdd(
                principal,
                _ => new PrincipalWindow(now, _policy.MaxConcurrentRequests));
        }
        lock (window)
        {
            window.LastSeen = now;
            if (now - window.Start >= TimeSpan.FromMinutes(1))
            {
                window.Start = now;
                window.Count = 0;
            }
            if (window.Count >= _policy.RequestsPerMinute || !window.Concurrency.Wait(0)) return false;
            window.Count++;
            window.Active++;
        }
        lease = new Release(window, _timeProvider);
        return true;
    }

    private void RemoveIdle(DateTimeOffset now)
    {
        foreach (var pair in _principals)
        {
            var remove = false;
            lock (pair.Value)
                remove = pair.Value.Active == 0 && now - pair.Value.LastSeen >= IdleRetention;
            if (remove) _principals.TryRemove(pair);
        }
    }

    private sealed class PrincipalWindow(DateTimeOffset start, int concurrency)
    {
        public DateTimeOffset Start = start;
        public DateTimeOffset LastSeen = start;
        public int Count;
        public int Active;
        public SemaphoreSlim Concurrency { get; } = new(concurrency, concurrency);
    }

    private sealed class Release(PrincipalWindow window, TimeProvider timeProvider) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (window)
            {
                window.Active--;
                window.LastSeen = timeProvider.GetUtcNow();
                window.Concurrency.Release();
            }
        }
    }
}
