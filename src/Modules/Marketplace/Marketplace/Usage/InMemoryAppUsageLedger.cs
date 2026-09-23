using System.Collections.Concurrent;

namespace DigitalBrain.Marketplace;

// Records which workspaces ran which apps. In production the broker reports runs; the catalog reads
// it to allow reviews only from workspaces that actually ran the app.
public sealed class InMemoryAppUsageLedger : IAppUsageLedger
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> runs = new(StringComparer.Ordinal);

    public void RecordRun(string appId, string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        runs.GetOrAdd(appId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))[workspaceId] = 0;
    }

    public bool HasRun(string appId, string workspaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return runs.TryGetValue(appId, out var workspaces) && workspaces.ContainsKey(workspaceId);
    }
}
