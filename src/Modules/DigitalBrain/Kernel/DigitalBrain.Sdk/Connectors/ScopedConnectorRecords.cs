using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Sdk.Connectors;

public static class ScopedConnectorRecords
{
    // A brain sees the records filed under it, plus the owner's own legacy records filed under the
    // account; a legacy record whose credential belongs to someone else is never shown.
    public static ConnectorRecord[] Combine(BrainScope scope, string principal, ConnectorRecord[] current, ConnectorRecord[] legacy)
        => [.. current.Where(record => record.WorkspaceId == scope.Name || record.WorkspaceId == scope.Id)
            .Concat(legacy.Where(record => (record.WorkspaceId == scope.Id || record.WorkspaceId == principal)
                && OwnedBy(record, principal)))
            .DistinctBy(record => record.Id)];

    private static bool OwnedBy(ConnectorRecord record, string principal)
    {
        try { return record.Credential.Owner == principal; }
        catch (InvalidOperationException) { return false; }
    }
}
