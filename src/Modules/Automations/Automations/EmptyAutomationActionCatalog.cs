namespace DigitalBrain.Automations;

// The Apps module owns the operation catalog. Until it registers one, no action validates, so
// every activation is rejected rather than silently running an unknown operation.
internal sealed class EmptyAutomationActionCatalog : IAutomationActionCatalog
{
    public Task<AutomationOperation?> FindAsync(string appId, string operation, CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationOperation?>(null);
}
