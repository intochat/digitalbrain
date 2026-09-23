namespace DigitalBrain.Automations;

// The app runtime arrives with the Apps module. A missing runtime is a recorded failure, never a
// silent success and never a retry.
internal sealed class UnavailableAutomationActionInvoker : IAutomationActionInvoker
{
    public Task<AutomationActionResult> InvokeAsync(AutomationDefinition definition, AutomationAction action, string intentId, CancellationToken cancellationToken = default)
        => Task.FromResult(new AutomationActionResult
        {
            Succeeded = false,
            Error = $"No app runtime is registered for {action.AppId}.{action.Operation}.",
        });
}
