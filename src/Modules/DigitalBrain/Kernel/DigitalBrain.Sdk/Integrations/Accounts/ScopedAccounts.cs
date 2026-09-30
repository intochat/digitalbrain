using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Sdk.Integrations.Accounts;

public static class ScopedAccounts
{
    public static IntegrationAccount[] Visible(BrainScope scope, IntegrationAccount[] records)
        => [.. records.Where(record => record.WorkspaceId == scope.Name || record.WorkspaceId == scope.Id).DistinctBy(record => record.Id)];
}
