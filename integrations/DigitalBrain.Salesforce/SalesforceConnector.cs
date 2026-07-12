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

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        var priorPending = await _store.GetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            cancellationToken);
        if (SalesforceClientFactory.TryGetReplayableAuthorizationChallenge(
                priorPending,
                out var replayUrl,
                out var replayState))
        {
            return new AuthChallenge(replayUrl, IsForm: false, State: replayState);
        }

        var flowId = priorPending.TryGetValue(SalesforceClientFactory.OAuthFlowIdKey, out var priorFlowId) &&
                     SalesforceClientFactory.IsAuthorizationFlowId(priorFlowId)
            ? priorFlowId
            : SalesforceClientFactory.CreateAuthorizationFlowId();

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

        var providerExpiresAt = DateTimeOffset.UtcNow
            .Add(SalesforceClientFactory.OAuthPendingLifetime)
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        var pendingValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SalesforceClientFactory.OAuthPhaseKey] = SalesforceClientFactory.OAuthPhaseChallengeIssued,
            [SalesforceClientFactory.OAuthFlowIdKey] = flowId,
            [SalesforceClientFactory.OAuthStateKey] = state,
            [SalesforceClientFactory.OAuthAttemptFingerprintKey] =
                SalesforceClientFactory.AuthorizationAttemptFingerprint(state),
            [SalesforceClientFactory.OAuthAuthorizationUrlKey] = url,
            [SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier,
            [SalesforceClientFactory.OAuthPendingClientIdKey] = clientId,
            [SalesforceClientFactory.OAuthPendingLoginUrlKey] = loginUrl,
            [SalesforceClientFactory.OAuthPendingRedirectUriKey] = redirectUri,
            [SalesforceClientFactory.OAuthPendingExpiresAtKey] = providerExpiresAt
        };
        foreach (var key in new[]
                 {
                     SalesforceClientFactory.OAuthStartTokenKey,
                     SalesforceClientFactory.OAuthStartTokenFingerprintKey,
                     SalesforceClientFactory.OAuthStartExpiresAtKey
                 })
        {
            if (priorPending.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                pendingValues[key] = value;
        }
        if (pendingValues.ContainsKey(SalesforceClientFactory.OAuthStartTokenKey))
            pendingValues[SalesforceClientFactory.OAuthStartExpiresAtKey] = providerExpiresAt;
        await _store.SetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            pendingValues,
            cancellationToken);

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
        var existingUser = await _store.GetAsync(userScope, SalesforceClientFactory.PackName, cancellationToken);

        if (IsPendingExpired(pending))
        {
            await TerminalizePendingAsync(userScope, pending, "expired");
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!pending.TryGetValue(SalesforceClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            var callbackFingerprint = SalesforceClientFactory.AuthorizationAttemptFingerprint(state);
            var mergedCredentials = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            foreach (var item in existingUser)
                mergedCredentials[item.Key] = item.Value;
            if (existingUser.TryGetValue(SalesforceClientFactory.OAuthCompletedFingerprintKey, out var completedFingerprint) &&
                SalesforceClientFactory.SameAuthorizationAttempt(callbackFingerprint, completedFingerprint) &&
                SalesforceClientFactory.HasUsableCredential(mergedCredentials))
            {
                return new AuthResult(true);
            }
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (pending.TryGetValue(SalesforceClientFactory.OAuthPhaseKey, out var phase) &&
            (!string.Equals(phase, SalesforceClientFactory.OAuthPhaseChallengeIssued, StringComparison.Ordinal) &&
             !string.Equals(phase, SalesforceClientFactory.OAuthPhaseProcessing, StringComparison.Ordinal) ||
             !pending.TryGetValue(SalesforceClientFactory.OAuthFlowIdKey, out var phaseFlowId) ||
             !SalesforceClientFactory.IsAuthorizationFlowId(phaseFlowId)))
        {
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            return new AuthResult(false, "state-mismatch", "State did not match.");
        }
        var attemptFingerprint = SalesforceClientFactory.AuthorizationAttemptFingerprint(expectedState);
        var flowId = pending.TryGetValue(SalesforceClientFactory.OAuthFlowIdKey, out var persistedFlowId) &&
                     SalesforceClientFactory.IsAuthorizationFlowId(persistedFlowId)
            ? persistedFlowId
            : null;
        if (!pending.TryGetValue(SalesforceClientFactory.OAuthAttemptFingerprintKey, out var persistedFingerprint) ||
            !SalesforceClientFactory.SameAuthorizationAttempt(persistedFingerprint, attemptFingerprint))
            return new AuthResult(false, "state-mismatch", "State did not match.");
        var completedMatches = existingUser.TryGetValue(
                                   SalesforceClientFactory.OAuthCompletedFingerprintKey,
                                   out var completedFingerprintForReplay) &&
                               SalesforceClientFactory.SameAuthorizationAttempt(
                                   attemptFingerprint,
                                   completedFingerprintForReplay);
        var completedFlowMatches = flowId is null ||
                                   existingUser.TryGetValue(
                                       SalesforceClientFactory.OAuthCompletedFlowIdKey,
                                       out var completedFlowForReplay) &&
                                   string.Equals(flowId, completedFlowForReplay, StringComparison.Ordinal);
        if (completedMatches && completedFlowMatches)
        {
            var mergedCredentials = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            foreach (var item in existingUser)
                mergedCredentials[item.Key] = item.Value;
            if (SalesforceClientFactory.HasUsableCredential(mergedCredentials))
                return new AuthResult(true);
        }

        await SetPendingResultAsync(userScope, pending, "processing", attemptFingerprint, flowId);

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            var denied = string.Equals(callback.Error, "access_denied", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(callback.Error, "user_denied_authorization", StringComparison.OrdinalIgnoreCase);
            await SetPendingResultAsync(userScope, pending, denied ? "denied" : "provider-error", attemptFingerprint, flowId);
            return denied
                ? new AuthResult(false, "consent-denied", "Salesforce consent was denied.")
                : new AuthResult(false, "provider-error", "Salesforce authorization failed.");
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            await SetPendingResultAsync(userScope, pending, "incomplete", attemptFingerprint, flowId);
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
            await SetPendingResultAsync(userScope, pending, "configuration-changed", attemptFingerprint, flowId);
            return new AuthResult(false, "configuration-changed", "Salesforce configuration changed. Start authorization again.");
        }

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
            userTokenValues[SalesforceClientFactory.OAuthCompletedFingerprintKey] = attemptFingerprint;
            if (flowId is not null)
                userTokenValues[SalesforceClientFactory.OAuthCompletedFlowIdKey] = flowId;

            await _store.SetAsync(userScope, SalesforceClientFactory.PackName, userTokenValues, cancellationToken);
            await BestEffortClearPendingAsync(userScope);

            return new AuthResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SetPendingResultAsync(userScope, pending, "cancelled", attemptFingerprint, flowId);
            throw;
        }
        catch (Exception)
        {
            await SetPendingResultAsync(userScope, pending, "exchange-failed", attemptFingerprint, flowId);
            return new AuthResult(false, "exchange-failed", "The authorization code exchange failed.");
        }
    }

    private Task SetPendingResultAsync(
        string userScope,
        IReadOnlyDictionary<string, string> pending,
        string result,
        string attemptFingerprint,
        string? flowId)
    {
        var processing = string.Equals(result, "processing", StringComparison.Ordinal);
        var values = processing
            ? new Dictionary<string, string>(pending, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        values[SalesforceClientFactory.OAuthPhaseKey] = processing
            ? SalesforceClientFactory.OAuthPhaseProcessing
            : SalesforceClientFactory.OAuthPhaseFailed;
        values[SalesforceClientFactory.OAuthResultKey] = result;
        values[SalesforceClientFactory.OAuthAttemptFingerprintKey] = attemptFingerprint;
        values[SalesforceClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
            .Add(SalesforceClientFactory.OAuthPendingLifetime)
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        if (flowId is not null)
            values[SalesforceClientFactory.OAuthFlowIdKey] = flowId;
        if (processing)
            values[SalesforceClientFactory.OAuthProcessingExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(SalesforceClientFactory.OAuthProcessingLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        return _store.SetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            values,
            CancellationToken.None);
    }

    private async Task BestEffortClearPendingAsync(string userScope)
    {
        try
        {
            await ClearPendingAsync(userScope);
        }
        catch (Exception)
        {
            // The credential pack contains a matching completion witness for this exact flow.
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
            if (pending.TryGetValue(SalesforceClientFactory.OAuthPhaseKey, out var phase) &&
                string.Equals(phase, SalesforceClientFactory.OAuthPhaseFailed, StringComparison.Ordinal))
            {
                var compact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [SalesforceClientFactory.OAuthPhaseKey] = SalesforceClientFactory.OAuthPhaseFailed,
                    [SalesforceClientFactory.OAuthResultKey] = pending.GetValueOrDefault(
                        SalesforceClientFactory.OAuthResultKey,
                        "expired")
                };
                if (pending.TryGetValue(SalesforceClientFactory.OAuthFlowIdKey, out var flowId) &&
                    SalesforceClientFactory.IsAuthorizationFlowId(flowId))
                    compact[SalesforceClientFactory.OAuthFlowIdKey] = flowId;
                if (pending.TryGetValue(SalesforceClientFactory.OAuthAttemptFingerprintKey, out var attempt) &&
                    SalesforceClientFactory.IsAuthorizationAttemptFingerprint(attempt))
                    compact[SalesforceClientFactory.OAuthAttemptFingerprintKey] = attempt;
                await _store.SetAsync(
                    userScope,
                    SalesforceClientFactory.OAuthPendingPackName,
                    compact,
                    CancellationToken.None);
            }
            else
            {
                await TerminalizePendingAsync(userScope, pending, "expired");
            }
        }
    }

    private Task TerminalizePendingAsync(
        string userScope,
        IReadOnlyDictionary<string, string> pending,
        string result)
    {
        var terminal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SalesforceClientFactory.OAuthPhaseKey] = SalesforceClientFactory.OAuthPhaseFailed,
            [SalesforceClientFactory.OAuthResultKey] = result,
            [SalesforceClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(SalesforceClientFactory.OAuthPendingLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture)
        };
        if (pending.TryGetValue(SalesforceClientFactory.OAuthFlowIdKey, out var flowId) &&
            SalesforceClientFactory.IsAuthorizationFlowId(flowId))
            terminal[SalesforceClientFactory.OAuthFlowIdKey] = flowId;
        if (pending.TryGetValue(SalesforceClientFactory.OAuthAttemptFingerprintKey, out var attempt) &&
            SalesforceClientFactory.IsAuthorizationAttemptFingerprint(attempt))
            terminal[SalesforceClientFactory.OAuthAttemptFingerprintKey] = attempt;
        return _store.SetAsync(
            userScope,
            SalesforceClientFactory.OAuthPendingPackName,
            terminal,
            CancellationToken.None);
    }

    private static bool IsPendingExpired(IReadOnlyDictionary<string, string> pending) =>
        SalesforceClientFactory.IsKnownPendingExpired(pending);

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
        catch (Exception)
        {
            return new ConnectionHealth(
                Healthy: false,
                Detail: "Salesforce connection probe failed.",
                Checked: DateTimeOffset.UtcNow);
        }
    }
}
