using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Kernel.Contracts.Runtime;

namespace DigitalBrain.Integrations.Google;

internal sealed class GoogleAuthorizationResolver(IIntegrationConfigStore store) : IExternalAuthorizationResolver
{
    public string Provider => OAuthCallbackPaths.GoogleProvider;

    public async Task<ExternalAuthorizationResolution> ResolveAsync(BrainOwnerId ownerId, ActorId actorId, CancellationToken cancellationToken = default)
    {
        var scope = new NeuronScope(new UserId(ownerId.Value), null);
        var userScope = IntegrationConfigScopes.ForUser(scope.UserId);
        var credentials = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        var pending = await store.GetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, cancellationToken);
        return GoogleClientFactory.ResolveAuthorization(credentials, pending);
    }
}
