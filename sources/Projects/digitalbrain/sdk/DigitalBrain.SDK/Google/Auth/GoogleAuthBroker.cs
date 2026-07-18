using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

namespace DigitalBrain.SDK.Google.Auth;

public sealed class GoogleAuthBroker(
    IConfiguration configuration,
    IGrainFactory grains,
    ITokenProtector protector,
    ILogger<GoogleAuthBroker> logger) : IGoogleAuthBroker
{
    public async Task<bool> HasStoredTokenAsync(
        string userAccountId, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        var dataStore = new DigitalBrainGrainDataStore(grains, protector, userAccountId);
        try
        {
            var existing = await dataStore.GetAsync<TokenResponse>("user");
            return existing is not null && !string.IsNullOrEmpty(existing.RefreshToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read stored token for {User}", userAccountId);
            return false;
        }
    }

    public async Task AuthorizeAsync(
        string userAccountId, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        var secrets = await LoadClientSecretsAsync(ct)
            ?? throw new InvalidOperationException(
                "Google OAuth client secrets are not configured. " +
                "Set DigitalBrain:Google:OAuthClientJsonPath via user secrets on the AppHost.");

        var dataStore = new DigitalBrainGrainDataStore(grains, protector, userAccountId);
        await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, scopes, userAccountId, ct, dataStore);
    }

    public async Task<UserCredential?> GetCredentialAsync(
        string userAccountId, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        var secrets = await LoadClientSecretsAsync(ct);
        if (secrets is null) return null;

        var dataStore = new DigitalBrainGrainDataStore(grains, protector, userAccountId);
        var existing = await dataStore.GetAsync<TokenResponse>("user");
        if (existing is null || string.IsNullOrEmpty(existing.RefreshToken)) return null;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = scopes,
            DataStore = dataStore,
        });
        return new UserCredential(flow, userAccountId, existing);
    }

    async Task<ClientSecrets?> LoadClientSecretsAsync(CancellationToken ct)
    {
        var path = configuration["DigitalBrain:Google:OAuthClientJsonPath"];
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            logger.LogWarning(
                "Google OAuth client secrets not configured (path={Path})", path ?? "<unset>");
            return null;
        }

        await using var stream = File.OpenRead(path);
        return (await GoogleClientSecrets.FromStreamAsync(stream)).Secrets;
    }
}
