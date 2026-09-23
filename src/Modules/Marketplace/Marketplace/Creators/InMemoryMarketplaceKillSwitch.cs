using System.Collections.Concurrent;

namespace DigitalBrain.Marketplace.Creators;

// The P5a kill switch is process-local: the product is one silo until T6. It acts immediately and
// carries the reason shown to the publisher. A durable, cross-silo version is P4.2/P5b work.
public sealed class InMemoryMarketplaceKillSwitch : IMarketplaceKillSwitch
{
    private readonly ConcurrentDictionary<string, KillSwitchStatus> _killed = new(StringComparer.Ordinal);

    public bool IsKilled(string appId) => !string.IsNullOrWhiteSpace(appId) && _killed.ContainsKey(appId);

    public KillSwitchStatus? Status(string appId) =>
        !string.IsNullOrWhiteSpace(appId) && _killed.TryGetValue(appId, out var status) ? status : null;

    public KillSwitchStatus Kill(string appId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var status = new KillSwitchStatus { AppId = appId, Reason = reason, KilledAt = DateTimeOffset.UtcNow };
        _killed[appId] = status;
        return status;
    }

    public void Restore(string appId)
    {
        if (!string.IsNullOrWhiteSpace(appId)) { _killed.TryRemove(appId, out _); }
    }
}
