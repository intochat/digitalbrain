using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Kernel.Contracts.Runtime;

namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceAuthorizationResolver(IIntegrationConfigStore store) : IExternalAuthorizationResolver
{
    public string Provider => OAuthCallbackPaths.SalesforceProvider;

    public async Task<ExternalAuthorizationResolution> ResolveAsync(BrainOwnerId ownerId, ActorId actorId, CancellationToken cancellationToken = default)
    {
        var scope = new NeuronScope(new UserId(ownerId.Value), null);
        var userScope = IntegrationConfigScopes.ForUser(scope.UserId);
        var credentials = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        var pending = await store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, cancellationToken);
        return SalesforceClientFactory.ResolveAuthorization(credentials, pending);
    }
}
