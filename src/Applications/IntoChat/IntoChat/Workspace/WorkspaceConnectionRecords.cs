using DigitalBrain.Sdk.Connectors;
using DigitalBrain.Identity;

namespace IntoChat.Workspace;

internal static class WorkspaceConnectionRecords
{
    internal static ConnectorRecord[] Combine(WorkspaceScope scope, string principal, ConnectorRecord[] current, ConnectorRecord[] legacy)
        => [.. current.Where(record => record.WorkspaceId == scope.WorkspaceId || record.WorkspaceId == scope.Id)
            .Concat(legacy.Where(record => (record.WorkspaceId == scope.Id || record.WorkspaceId == principal)
                && OwnedBy(record, principal)))
            .DistinctBy(record => record.Id)];

    private static bool OwnedBy(ConnectorRecord record, string principal)
    {
        try { return record.Credential.Owner == principal; }
        catch (InvalidOperationException) { return false; }
    }
}
