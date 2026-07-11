using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using System.Globalization;

namespace DigitalBrain.Salesforce;

/// IConnector implementation for Salesforce (P2 Phase 1).
/// Uses store for config, factory for client. Health uses probe query. Auth uses store/PKCE logic (adapted from neuron).
public class SalesforceConnector : IConnector
{
    private readonly ISalesforceApiClientFactory _factory;
    private readonly IPackConfigStore _store;
    private readonly IOAuthStateProtector _stateProtector;
    private readonly HttpMessageHandler? _tokenEndpointHandler;

    public SalesforceConnector(
        ISalesforceApiClientFactory factory,
        IPackConfigStore store,
        IOAuthStateProtector stateProtector,
        HttpMessageHandler? tokenEndpointHandler = null)
    {
        _factory = factory;
        _store = store;
        _stateProtector = stateProtector;
        _tokenEndpointHandler = tokenEndpointHandler;
    }

    public ConnectorDescriptor Descriptor => new(
        Id: "salesforce",
        DisplayName: "Salesforce CRM",
        RequiredConfigKeys: new[] { SalesforceClientFactory.ClientIdKey, SalesforceClientFactory.ClientSecretKey },
        Scopes: new[] { "api", "refresh_token" });

    public async Task<ConnectorConfigStatus> ValidateConfigAsync(string? userScope = null, CancellationToken cancellationToken = default)
    {
        var values = await _store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(userScope))
        {
            await ClearExpiredPendingAsync(userScope, cancellationToken);
        }

        return SalesforceClientFactory.TryValidateAppConfig(values, out var invalidKey, out var message)
            ? new ConnectorConfigStatus(true)
            : new ConnectorConfigStatus(false, MissingKey: invalidKey, Message: message);
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

        if (!SalesforceClientFactory.TryValidateAppConfig(values, out _, out _))
        {
            return new AuthChallenge(UrlOrForm: "credential-form-needed", IsForm: true);
        }

        var redirectUri = SalesforceClientFactory.ResolveRedirectUri(values);
        var loginUrl = SalesforceClientFactory.ResolveLoginUrl(values);
        var clientId = values[SalesforceClientFactory.ClientIdKey].Trim();

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

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        await _store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.OAuthStateKey] = state,
            [SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier,
            [SalesforceClientFactory.OAuthPendingClientIdKey] = clientId,
            [SalesforceClientFactory.OAuthPendingLoginUrlKey] = loginUrl,
            [SalesforceClientFactory.OAuthPendingRedirectUriKey] = redirectUri,
            [SalesforceClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(SalesforceClientFactory.OAuthPendingLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

        return new AuthChallenge(url, IsForm: false, State: state);
    }

    public async Task<AuthResult> CompleteAuthAsync(OAuthCallback callback, CancellationToken cancellationToken = default)
    {
        var state = callback.State;
        if (!_stateProtector.TryUnprotect(state, out var user))
            return new AuthResult(false, "invalid-state", "The authorization state is invalid or expired.");

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));

        var appValues = await _store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, cancellationToken);
        var pending = await _store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, cancellationToken);

        if (IsPendingExpired(pending))
        {
            await ClearPendingAsync(userScope);
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!pending.TryGetValue(SalesforceClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            return new AuthResult(false, "state-mismatch", "State did not match.");
        }

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            await ClearPendingAsync(userScope);
            return string.Equals(callback.Error, "access_denied", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(callback.Error, "user_denied_authorization", StringComparison.OrdinalIgnoreCase)
                ? new AuthResult(false, "consent-denied", "Salesforce consent was denied.")
                : new AuthResult(false, "provider-error", "Salesforce authorization failed.");
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            await ClearPendingAsync(userScope);
            return new AuthResult(false, "no-code", "The callback did not include an authorization code.");
        }

        if (!pending.TryGetValue(SalesforceClientFactory.OAuthPendingClientIdKey, out var pendingClientId) ||
            string.IsNullOrWhiteSpace(pendingClientId) ||
            !pending.TryGetValue(SalesforceClientFactory.OAuthPendingLoginUrlKey, out var pendingLoginUrl) ||
            string.IsNullOrWhiteSpace(pendingLoginUrl) ||
            !pending.TryGetValue(SalesforceClientFactory.OAuthPendingRedirectUriKey, out var pendingRedirectUri) ||
            string.IsNullOrWhiteSpace(pendingRedirectUri) ||
            !pending.TryGetValue(SalesforceClientFactory.OAuthCodeVerifierKey, out var codeVerifier) ||
            string.IsNullOrWhiteSpace(codeVerifier) ||
            !SalesforceClientFactory.HasConnectedAppConfig(appValues) ||
            !appValues.TryGetValue(SalesforceClientFactory.ClientIdKey, out var currentClientId) ||
            !string.Equals(pendingClientId, currentClientId?.Trim(), StringComparison.Ordinal))
        {
            await ClearPendingAsync(userScope);
            return new AuthResult(false, "configuration-changed", "Salesforce configuration changed. Start authorization again.");
        }

        await ClearPendingAsync(userScope);

        try
        {
            var exchangeValues = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            exchangeValues[SalesforceClientFactory.ClientIdKey] = pendingClientId.Trim();
            exchangeValues[SalesforceClientFactory.LoginUrlKey] = pendingLoginUrl.Trim();
            exchangeValues[SalesforceClientFactory.RedirectUriKey] = pendingRedirectUri.Trim();
            exchangeValues[SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier.Trim();

            var tokenValues = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(
                exchangeValues,
                callback.Code,
                pendingRedirectUri,
                _tokenEndpointHandler,
                cancellationToken);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tokenValues)
            {
                userTokenValues[kv.Key] = kv.Value;
            }

            await _store.SetAsync(userScope, SalesforceClientFactory.PackName, userTokenValues, cancellationToken);

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

    private Task ClearPendingAsync(string userScope) =>
        _store.SetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            new Dictionary<string, string>(),
            CancellationToken.None);

    private async Task ClearExpiredPendingAsync(string userScope, CancellationToken cancellationToken)
    {
        var pending = await _store.GetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            cancellationToken);
        if (pending.Count > 0 && IsPendingExpired(pending))
        {
            await ClearPendingAsync(userScope);
        }
    }

    private static bool IsPendingExpired(IReadOnlyDictionary<string, string> pending) =>
        !pending.TryGetValue(SalesforceClientFactory.OAuthPendingExpiresAtKey, out var expiresAt) ||
        !long.TryParse(expiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnixSeconds) ||
        expiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task<ConnectionHealth> TestConnectionAsync(NeuronId user, CancellationToken cancellationToken = default)
    {
        try
        {
            await ClearExpiredPendingAsync(
                PackConfigScopes.ForUser(new UserId(user.Value)),
                cancellationToken);
            // Use existing factory to create client (exercises merged scope/credentials).
            var client = await _factory.CreateAsync(new NeuronScope(new UserId(user.Value), null), cancellationToken);  // per-user scope for credential merge
            await client.ListAccountsAsync(1, cancellationToken);
            return new ConnectionHealth(Healthy: true, Detail: "Salesforce connection healthy (account read succeeded)", Checked: DateTimeOffset.UtcNow);
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
