using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Salesforce;

/// IConnector implementation for Salesforce (P2 Phase 1).
/// Uses store for config, factory for client. Health uses probe query. Auth uses store/PKCE logic (adapted from neuron).
public class SalesforceConnector : IConnector
{
    private readonly ISalesforceApiClientFactory _factory;
    private readonly IPackConfigStore _store;
    private readonly IOAuthStateProtector _stateProtector;
    private readonly IConfiguration? _config;
    private readonly HttpMessageHandler? _tokenEndpointHandler;

    public SalesforceConnector(
        ISalesforceApiClientFactory factory,
        IPackConfigStore store,
        IOAuthStateProtector stateProtector,
        IConfiguration? config = null,
        HttpMessageHandler? tokenEndpointHandler = null)
    {
        _factory = factory;
        _store = store;
        _stateProtector = stateProtector;
        _config = config;
        _tokenEndpointHandler = tokenEndpointHandler;
    }

    public ConnectorDescriptor Descriptor => new(
        Id: "salesforce",
        DisplayName: "Salesforce CRM",
        RequiredConfigKeys: new[] { SalesforceClientFactory.ClientIdKey, SalesforceClientFactory.ClientSecretKey, SalesforceClientFactory.LoginUrlKey, SalesforceClientFactory.ApiVersionKey, SalesforceClientFactory.OAuthScopeKey, SalesforceClientFactory.RedirectUriKey },
        Scopes: new[] { "api", "refresh_token" });

    public async Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null, CancellationToken cancellationToken = default)
    {
        var scope = string.IsNullOrWhiteSpace(userScope) ? PackConfigScopes.App : userScope;
        var values = await _store.GetAsync(scope, SalesforceClientFactory.PackName, cancellationToken);
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
        var existing = await _store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, cancellationToken);
        var values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        // props would come from clientIdHint or elsewhere; for simplicity use config
        if (clientIdHint != null)
        {
            values[SalesforceClientFactory.ClientIdKey] = clientIdHint;
        }

        var configuredRedirectUri = _config?["DigitalBrain:Salesforce:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(configuredRedirectUri))
        {
            values[SalesforceClientFactory.RedirectUriKey] = configuredRedirectUri.Trim();
        }
        else if (!values.ContainsKey(SalesforceClientFactory.RedirectUriKey))
        {
            values[SalesforceClientFactory.RedirectUriKey] = SalesforceClientFactory.DefaultRedirectUri;
        }

        if (!values.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var redirectUri) || string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = SalesforceClientFactory.DefaultRedirectUri;
            values[SalesforceClientFactory.RedirectUriKey] = redirectUri;
        }

        if (!SalesforceClientFactory.HasConnectedAppConfig(values))
        {
            return new AuthChallenge(UrlOrForm: "credential-form-needed", IsForm: true);
        }

        var state = _stateProtector.Protect(user);
        var codeVerifier = SalesforceClientFactory.CreatePkceCodeVerifier();
        var codeChallenge = SalesforceClientFactory.CreatePkceCodeChallenge(codeVerifier);

        string url;
        try
        {
            url = SalesforceClientFactory.CreateAuthorizationUrl(values, redirectUri, state, codeChallenge);
        }
        catch (InvalidOperationException ex)
        {
            return new AuthChallenge(UrlOrForm: "error:" + ex.Message, IsForm: true);
        }

        await _store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, values, cancellationToken);
        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        await _store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.OAuthStateKey] = state,
            [SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier
        }, cancellationToken);

        return new AuthChallenge(url, IsForm: false, State: state);
    }

    public async Task<AuthResult> CompleteAuthAsync(OAuthCallback callback, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            return string.Equals(callback.Error, "access_denied", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(callback.Error, "user_denied_authorization", StringComparison.OrdinalIgnoreCase)
                ? new AuthResult(false, "consent-denied", "Salesforce consent was denied.")
                : new AuthResult(false, "provider-error", "Salesforce authorization failed.");
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return new AuthResult(false, "no-code", "The callback did not include an authorization code.");
        }

        var state = callback.State;
        if (!_stateProtector.TryUnprotect(state, out var user))
            return new AuthResult(false, "invalid-state", "The authorization state is invalid or expired.");

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));

        var appValues = await _store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, cancellationToken);
        var pending = await _store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, cancellationToken);

        if (!pending.TryGetValue(SalesforceClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            return new AuthResult(false, "state-mismatch", "State did not match.");
        }

        var redirectUri = appValues.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var stored) ? stored : callback.FallbackRedirectUri;
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = SalesforceClientFactory.DefaultRedirectUri;
        }

        try
        {
            var exchangeValues = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            if (pending.TryGetValue(SalesforceClientFactory.OAuthCodeVerifierKey, out var codeVerifier) &&
                !string.IsNullOrWhiteSpace(codeVerifier))
            {
                exchangeValues[SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier;
            }

            var tokenValues = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(
                exchangeValues,
                callback.Code,
                redirectUri,
                _tokenEndpointHandler,
                cancellationToken);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tokenValues)
            {
                userTokenValues[kv.Key] = kv.Value;
            }

            if (appValues.TryGetValue(SalesforceClientFactory.ClientIdKey, out var cid))
            {
                userTokenValues[SalesforceClientFactory.ClientIdKey] = cid;
            }

            if (appValues.TryGetValue(SalesforceClientFactory.ClientSecretKey, out var cs))
            {
                userTokenValues[SalesforceClientFactory.ClientSecretKey] = cs;
            }

            await _store.SetAsync(userScope, SalesforceClientFactory.PackName, userTokenValues, cancellationToken);
            await _store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>(), cancellationToken);

            return new AuthResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new AuthResult(false, "exchange-failed", "The authorization code exchange failed.");
        }
    }

    public async Task<ConnectionHealth> TestConnectionAsync(NeuronId user, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use existing factory to create client (exercises merged scope/credentials).
            var client = await _factory.CreateAsync(new NeuronScope(new UserId(user.Value), null), cancellationToken);  // per-user scope for credential merge
            // Cheap probe: SELECT Id FROM User LIMIT 1
            await client.QueryAsync("SELECT Id FROM User LIMIT 1", cancellationToken);
            return new ConnectionHealth(Healthy: true, Detail: "Salesforce connection healthy (query succeeded)", Checked: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ConnectionHealth(Healthy: false, Detail: ex.Message, Checked: DateTimeOffset.UtcNow);
        }
    }
}
