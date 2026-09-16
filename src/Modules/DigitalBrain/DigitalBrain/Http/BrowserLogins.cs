using System.Security.Cryptography;

namespace DigitalBrain.Core;

public abstract class BrowserLogins(BrowserLoginDefinition definition)
{
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    public BrowserLoginDefinition Definition { get; } = definition ?? throw new ArgumentNullException(nameof(definition));

    // Null until the operator has supplied the provider's OAuth client; no login can start before.
    protected abstract Uri? PublicOrigin { get; }

    internal Uri? ConfiguredOrigin => PublicOrigin;

    public Uri Require(string? scope = null)
    {
        var origin = PublicOrigin
            ?? throw new InvalidOperationException($"{Definition.DisplayName} setup is incomplete. Configure its OAuth client privately in Aspire.");

        lock (_pending)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _pending.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray())
            {
                _pending.Remove(key);
            }

            if (_pending.Count >= Definition.Capacity)
            {
                throw new InvalidOperationException($"Too many pending {Definition.DisplayName} logins. Wait for an existing request to expire.");
            }

            var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            _pending.Add(id, new Pending(now.Add(Definition.Lifetime), scope));
            return new Uri(origin, $"{Definition.LoginPath}?request={id}");
        }
    }

    internal bool TryBegin(string? id, out string? scope)
    {
        lock (_pending)
        {
            scope = null;
            if (id is null || !_pending.TryGetValue(id, out var pending)
                || pending.ExpiresAt <= DateTimeOffset.UtcNow || pending.Begun)
            {
                return false;
            }

            pending.Begun = true;
            scope = pending.Scope;
            return true;
        }
    }

    internal bool TryClaim(string? id)
    {
        lock (_pending)
        {
            if (id is null || !_pending.TryGetValue(id, out var pending)
                || pending.ExpiresAt <= DateTimeOffset.UtcNow || !pending.Begun)
            {
                return false;
            }

            return _pending.Remove(id);
        }
    }

    public void Reject(string? id)
    {
        lock (_pending)
        {
            if (id is not null)
            {
                _pending.Remove(id);
            }
        }
    }

    private sealed class Pending(DateTimeOffset expiresAt, string? scope)
    {
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal string? Scope { get; } = scope;
        internal bool Begun { get; set; }
    }
}
