using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;

namespace DigitalBrain.Integrations.Google;

internal sealed class GmailApiClientFactory(IIntegrationConfigStore store) : IGmailApiClientFactory
{
    public async Task<IGmailApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default)
    {
        var merged = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken).ConfigureAwait(false);

        if (!merged.TryGetValue(GoogleClientFactory.ClientIdKey, out var clientId) ||
            !merged.TryGetValue(GoogleClientFactory.ClientSecretKey, out var clientSecret) ||
            !merged.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var refreshToken))
        {
            throw new InvalidOperationException("Google pack config is missing required keys for Gmail. Complete sign in.");
        }

        var credential = GoogleCredentialFactory.FromRefreshToken(clientId, clientSecret, refreshToken, GoogleClientFactory.DefaultGmailScope, GoogleClientFactory.GmailSendScope);

        return new GoogleGmailApiClient(credential);
    }
}
