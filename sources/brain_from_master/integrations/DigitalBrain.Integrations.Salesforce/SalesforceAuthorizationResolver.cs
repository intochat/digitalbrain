using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Kernel.Contracts.Runtime;
namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceAuthorizationResolver(IIntegrationConfigStore store) : IExternalAuthorizationResolver
{
    public string Provider => SalesforceClientFactory.Provider;
    public string DisplayName => "Salesforce";
    public bool AllowsTool(string toolId) =>
        toolId.StartsWith("salesforce.", StringComparison.Ordinal) || toolId.StartsWith("cross.", StringComparison.Ordinal);
    public bool IsAllowedAuthorizationUrl(string? value) => SalesforceClientFactory.IsAllowedAuthorizationUrl(value);
    public async Task<ExternalAuthorizationResolution> ResolveAsync(BrainOwnerId ownerId, ActorId actorId, CancellationToken cancellationToken = default)
    {
        var scope = new NeuronScope(new UserId(ownerId.Value), null);
        var userScope = IntegrationConfigScopes.ForUser(scope.UserId);
        var credentials = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        var pending = await store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, cancellationToken);
        return SalesforceClientFactory.ResolveAuthorization(credentials, pending);
    }
}
