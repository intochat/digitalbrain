using Ino.Core.Hosting.Placement;
using Ino.Kernel.Contracts;
using Orleans;

namespace Ino.Kernel;

/// <summary>
/// Kernel-pinned per-user routing decision buffer. Keeps the last
/// <see cref="CapPerUser"/> decisions per user in newest-first order for
/// the inspector Routing tab. State is in-memory (v0.1).
/// </summary>
[PinToSilo("kernel")]
public sealed class CortexJournal : Grain, ICortexJournal
{
    private const int CapPerUser = 20;

    // userId -> circular buffer (newest at index 0 after each Record).
    private readonly Dictionary<string, LinkedList<RoutingDecision>> _byUser = new(StringComparer.Ordinal);

    public Task RecordAsync(string userId, RoutingDecision decision)
    {
        if (!_byUser.TryGetValue(userId, out var buf))
        {
            buf = new LinkedList<RoutingDecision>();
            _byUser[userId] = buf;
        }
        buf.AddFirst(decision);
        while (buf.Count > CapPerUser) buf.RemoveLast();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RoutingDecision>> GetRecentAsync(string userId, int count)
    {
        if (!_byUser.TryGetValue(userId, out var buf))
            return Task.FromResult<IReadOnlyList<RoutingDecision>>(Array.Empty<RoutingDecision>());
        IReadOnlyList<RoutingDecision> result = buf.Take(count).ToArray();
        return Task.FromResult(result);
    }
}
