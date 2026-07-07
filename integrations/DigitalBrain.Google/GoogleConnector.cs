using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Google;

/// IConnector implementation for Google (P2 Phase 1).
/// Uses store/factory for auth flow (adapted from neuron).
public class GoogleConnector : IConnector
{
    private readonly IPackConfigStore _store;
    private readonly IConfiguration? _config;

    public GoogleConnector(IPackConfigStore store, IConfiguration? config = null)
    {
        _store = store;
        _config = config;
    }

    public ConnectorDescriptor Descriptor => new(
        Id: "google",
        DisplayName: "Google",
        RequiredConfigKeys: new[] { GoogleClientFactory.ClientIdKey, GoogleClientFactory.ClientSecretKey, GoogleClientFactory.RedirectUriKey },
        Scopes: new[] { GoogleClientFactory.DefaultGmailScope });

    public async Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null)
    {
        var scope = string.IsNullOrWhiteSpace(userScope) ? GoogleClientFactory.DefaultScope : userScope;
        var values = await _store.GetAsync(scope, GoogleClientFactory.PackName);
        foreach (var key in Descriptor.RequiredConfigKeys)
        {
            if (!values.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
                return new ConnectorConfigStatus(false, MissingKey: key, Message: $"Missing {key}");
        }
        return new ConnectorConfigStatus(true);
    }

    public async Task<AuthChallenge> BeginAuthAsync(NeuronId user, string? clientIdHint = null)
    {
        IPackConfigStore? store = _store;
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (store is not null)
        {
            try
            {
                var existing = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName);
                values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        if (clientIdHint != null) values[GoogleClientFactory.ClientIdKey] = clientIdHint;

        var configuredRedirect = _config?["DigitalBrain:Google:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configuredRedirect))
            values[GoogleClientFactory.RedirectUriKey] = configuredRedirect.Trim();
        else if (values.TryGetValue(GoogleClientFactory.RedirectUriKey, out var r) && !string.IsNullOrWhiteSpace(r))
            values[GoogleClientFactory.RedirectUriKey] = r;
        else
            values[GoogleClientFactory.RedirectUriKey] = GoogleClientFactory.DefaultRedirectUri;

        if (!values.TryGetValue(GoogleClientFactory.RedirectUriKey, out var redirectUri) || string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = GoogleClientFactory.DefaultRedirectUri;
            values[GoogleClientFactory.RedirectUriKey] = redirectUri;
        }

        if (!GoogleClientFactory.HasConnectedAppConfig(values) || store is null)
        {
            return new AuthChallenge(UrlOrForm: "credential-form-needed", IsForm: true);
        }

        var state = $"{user.Value}:{Guid.NewGuid():N}";

        string authUrl;
        try
        {
            authUrl = GoogleClientFactory.CreateAuthorizationUrl(values, redirectUri, state);
        }
        catch (InvalidOperationException ex)
        {
            return new AuthChallenge(UrlOrForm: "error:" + ex.Message, IsForm: true);
        }

        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, values);

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.OAuthStateKey] = state
        });

        return new AuthChallenge(authUrl, IsForm: false, State: state);
    }

    public async Task<AuthResult> CompleteAuthAsync(OAuthCallback callback)
    {
        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            return new AuthResult(false, callback.Error, callback.ErrorDescription);
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return new AuthResult(false, "no-code", "The callback did not include an authorization code.");
        }

        var store = _store;
        var userId = callback.State?.Split(':')[0] ?? "default";
        var user = new NeuronId(userId);
        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        var appValues = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName);
        var pending = await store.GetAsync(userScope, GoogleClientFactory.OAuthPendingPackName);

        if (!pending.TryGetValue(GoogleClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!string.Equals(expectedState, callback.State, StringComparison.Ordinal))
        {
            return new AuthResult(false, "state-mismatch", "State did not match.");
        }

        var redirectUri = appValues.TryGetValue(GoogleClientFactory.RedirectUriKey, out var stored) ? stored : callback.FallbackRedirectUri;
        if (string.IsNullOrWhiteSpace(redirectUri))
            redirectUri = GoogleClientFactory.DefaultRedirectUri;

        try
        {
            var tokenValues = await GoogleClientFactory.ExchangeAuthorizationCodeAsync(appValues, callback.Code, redirectUri);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tokenValues)
            {
                userTokenValues[kv.Key] = kv.Value;
            }

            if (appValues.TryGetValue(GoogleClientFactory.ClientIdKey, out var cid))
                userTokenValues[GoogleClientFactory.ClientIdKey] = cid;
            if (appValues.TryGetValue(GoogleClientFactory.ClientSecretKey, out var cs))
                userTokenValues[GoogleClientFactory.ClientSecretKey] = cs;

            await store.SetAsync(userScope, GoogleClientFactory.PackName, userTokenValues);
            await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>());

            return new AuthResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AuthResult(false, "exchange-failed", ex.Message);
        }
    }

    public async Task<ConnectionHealth> TestConnectionAsync(NeuronId user)
    {
        try
        {
            var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
            var values = await _store.GetAsync(userScope, GoogleClientFactory.PackName);
            if (!values.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var rt) || string.IsNullOrWhiteSpace(rt))
                return new ConnectionHealth(Healthy: false, Detail: "No refresh token for user", Checked: DateTimeOffset.UtcNow);

            if (!values.TryGetValue(GoogleClientFactory.ClientIdKey, out var cid) || string.IsNullOrWhiteSpace(cid) ||
                !values.TryGetValue(GoogleClientFactory.ClientSecretKey, out var cs) || string.IsNullOrWhiteSpace(cs))
                return new ConnectionHealth(Healthy: false, Detail: "Missing client credentials for probe", Checked: DateTimeOffset.UtcNow);

            var cred = GoogleCredentialFactory.FromRefreshToken(cid, cs, rt, GoogleClientFactory.DefaultGmailScope);
            var service = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "DigitalBrain-TestConnection"
            });

            var labelsResponse = await service.Users.Labels.List("me").ExecuteAsync();
            var count = labelsResponse.Labels?.Count ?? 0;
            return new ConnectionHealth(Healthy: true, Detail: $"Google labels.list succeeded ({count} labels)", Checked: DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new ConnectionHealth(Healthy: false, Detail: "Probe failed: " + ex.Message, Checked: DateTimeOffset.UtcNow);
        }
    }
}