using DigitalBrain.Apps.Manifests;
using DigitalBrain.Automations;

namespace IntoChat.Apps;

// Resolves an automation action against the committed first-party manifests. A workspace-saved app
// supplies its own catalog later; these two operations are the durable jobs LeadGenerator needs.
internal sealed class FirstPartyAutomationActionCatalog : IAutomationActionCatalog
{
    public Task<AutomationOperation?> FindAsync(string appId, string operation, CancellationToken cancellationToken = default)
    {
        if (!FirstPartyApps.Contains(appId)) { return Task.FromResult<AutomationOperation?>(null); }
        var manifest = FirstPartyApps.Get(appId);
        var match = manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, operation, StringComparison.Ordinal));
        if (match is null) { return Task.FromResult<AutomationOperation?>(null); }
        var meter = manifest.Meters.FirstOrDefault();
        return Task.FromResult<AutomationOperation?>(new AutomationOperation
        {
            AppId = appId,
            Operation = operation,
            ReadOnly = match.ReadOnly,
            MeterId = meter?.MeterId,
            EstimatedCompute = meter?.ProposedPriceInCompute ?? 0m,
        });
    }
}

// Runs the LeadGenerator daily sweep on fakes. No paid step is retried automatically; the shadow
// Compute figure is not charged in v1.
internal sealed class LeadGeneratorAutomationInvoker(IGrainFactory grains) : IAutomationActionInvoker
{
    public async Task<AutomationActionResult> InvokeAsync(
        AutomationDefinition definition,
        AutomationAction action,
        string intentId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(action.AppId, "intochat.leadgenerator", StringComparison.Ordinal))
        {
            return new AutomationActionResult
            {
                Succeeded = false,
                Error = $"No app runtime is registered for {action.AppId}.{action.Operation}.",
            };
        }

        await grains.GetGrain<ILeadGeneratorLeads>(definition.WorkspaceId).Sweep("daily leads");
        return new AutomationActionResult { Succeeded = true, ActualCompute = 0m };
    }
}
