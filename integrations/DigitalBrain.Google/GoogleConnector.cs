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
    private readonly IGrainFactory? _grainFactory;
    private readonly HttpMessageHandler? _tokenEndpointHandler;

    public GoogleConnector(
        IPackConfigStore store,
        IConfiguration? config = null,
        IGrainFactory? grainFactory = null,
        HttpMessageHandler? tokenEndpointHandler = null)
    {
        _store = store;
        _config = config;
        _grainFactory = grainFactory;
        _tokenEndpointHandler = tokenEndpointHandler;
    }

    public ConnectorDescriptor Descriptor => new(
        Id: "google",
        DisplayName: "Google",
        RequiredConfigKeys: new[] { GoogleClientFactory.ClientIdKey, GoogleClientFactory.ClientSecretKey, GoogleClientFactory.RedirectUriKey },
        Scopes: new[] { GoogleClientFactory.DefaultGmailScope });

    public async Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null, CancellationToken cancellationToken = default)
    {
        var scope = string.IsNullOrWhiteSpace(userScope) ? GoogleClientFactory.DefaultScope : userScope;
        var values = await _store.GetAsync(scope, GoogleClientFactory.PackName, cancellationToken);
        foreach (var key in Descriptor.RequiredConfigKeys)
        {
            if (!values.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
            {
                return new ConnectorConfigStatus(false, MissingKey: key, Message: $"Missing {key}");
            }
        }
        return new ConnectorConfigStatus(true);
    }

    public async Task<AuthChallenge> BeginAuthAsync(NeuronId user, string? clientIdHint = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IPackConfigStore? store = _store;
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (store is not null)
        {
            try
            {
                var existing = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, cancellationToken);
                values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { }
        }

        if (clientIdHint != null)
        {
            values[GoogleClientFactory.ClientIdKey] = clientIdHint;
        }

        var configuredRedirect = _config?["DigitalBrain:Google:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configuredRedirect))
        {
            values[GoogleClientFactory.RedirectUriKey] = configuredRedirect.Trim();
        }
        else
        {
            values[GoogleClientFactory.RedirectUriKey] = values.TryGetValue(GoogleClientFactory.RedirectUriKey, out var r) && !string.IsNullOrWhiteSpace(r)
                ? r
                : GoogleClientFactory.DefaultRedirectUri;
        }

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

        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, values, cancellationToken);

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.OAuthStateKey] = state
        }, cancellationToken);

        return new AuthChallenge(authUrl, IsForm: false, State: state);
    }

    public async Task<AuthResult> CompleteAuthAsync(OAuthCallback callback, CancellationToken cancellationToken = default)
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
        var appValues = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, cancellationToken);
        var pending = await store.GetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, cancellationToken);

        if (!pending.TryGetValue(GoogleClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!string.Equals(expectedState, callback.State, StringComparison.Ordinal))
        {
            return new AuthResult(false, "state-mismatch", "State did not match.");
        }

        var redirectUri = appValues.TryGetValue(GoogleClientFactory.RedirectUriKey, out var stored) && !string.IsNullOrWhiteSpace(stored)
            ? stored
            : (!string.IsNullOrWhiteSpace(callback.FallbackRedirectUri) ? callback.FallbackRedirectUri : null);
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = _config?["DigitalBrain:Google:RedirectUri"] ?? GoogleClientFactory.DefaultRedirectUri;
        }

        var existingUser = await store.GetAsync(userScope, GoogleClientFactory.PackName, cancellationToken);

        try
        {
            var tokenValues = await GoogleClientFactory.ExchangeAuthorizationCodeAsync(appValues, callback.Code, redirectUri, _tokenEndpointHandler, cancellationToken);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tokenValues)
            {
                userTokenValues[kv.Key] = kv.Value;
            }

            if (appValues.TryGetValue(GoogleClientFactory.ClientIdKey, out var cid))
            {
                userTokenValues[GoogleClientFactory.ClientIdKey] = cid;
            }

            if (appValues.TryGetValue(GoogleClientFactory.ClientSecretKey, out var cs))
            {
                userTokenValues[GoogleClientFactory.ClientSecretKey] = cs;
            }

            if (!userTokenValues.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var newRt) || string.IsNullOrWhiteSpace(newRt))
            {
                if (existingUser.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var priorRt) && !string.IsNullOrWhiteSpace(priorRt))
                {
                    userTokenValues[GoogleClientFactory.RefreshTokenKey] = priorRt;
                }
            }

            await store.SetAsync(userScope, GoogleClientFactory.PackName, userTokenValues, cancellationToken);
            await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>(), cancellationToken);

            if (_grainFactory is not null)
            {
                var notifyKey = "google-auth-completed";
                var ingress = _grainFactory.GetGrain<IIngressNeuron>(notifyKey);
                var props = new Dictionary<string, object?>
                {
                    ["provider"] = "google",
                    ["pack"] = GoogleClientFactory.PackName,
                    ["userId"] = userId,
                    ["scope"] = userScope
                };
                await ingress.IngestAsync("PackConfigured", props, cancellationToken);
                await ingress.IngestAsync(GoogleSignals.AuthCompleted, props, cancellationToken);
            }

            return new AuthResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AuthResult(false, "exchange-failed", ex.Message);
        }
    }

    public async Task<ConnectionHealth> TestConnectionAsync(NeuronId user, CancellationToken cancellationToken = default)
    {
        try
        {
            var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
            var values = await _store.GetAsync(userScope, GoogleClientFactory.PackName, cancellationToken);
            if (!values.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var rt) || string.IsNullOrWhiteSpace(rt))
            {
                return new ConnectionHealth(Healthy: false, Detail: "No refresh token for user", Checked: DateTimeOffset.UtcNow);
            }

            if (!values.TryGetValue(GoogleClientFactory.ClientIdKey, out var cid) || string.IsNullOrWhiteSpace(cid) ||
                !values.TryGetValue(GoogleClientFactory.ClientSecretKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            {
                return new ConnectionHealth(Healthy: false, Detail: "Missing client credentials for probe", Checked: DateTimeOffset.UtcNow);
            }

            var cred = GoogleCredentialFactory.FromRefreshToken(cid, cs, rt, GoogleClientFactory.DefaultGmailScope);
            var service = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "DigitalBrain-TestConnection"
            });

            var labelsResponse = await service.Users.Labels.List("me").ExecuteAsync(cancellationToken);
            var count = labelsResponse.Labels?.Count ?? 0;
            return new ConnectionHealth(Healthy: true, Detail: $"Google labels.list succeeded ({count} labels)", Checked: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConnectionHealth(Healthy: false, Detail: "Probe failed: " + ex.Message, Checked: DateTimeOffset.UtcNow);
        }
    }
}
