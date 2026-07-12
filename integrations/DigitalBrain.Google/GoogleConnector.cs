using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Abstractions;
using System.Globalization;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Google;

/// IConnector implementation for Google (P2 Phase 1).
/// Uses store/factory for auth flow (adapted from neuron).
public class GoogleConnector : IConnector
{
    private readonly IPackConfigStore _store;
    private readonly IOAuthStateProtector _stateProtector;
    private readonly IConfiguration? _config;
    private readonly HttpMessageHandler? _tokenEndpointHandler;

    public GoogleConnector(
        IPackConfigStore store,
        IOAuthStateProtector stateProtector,
        IConfiguration? config = null,
        HttpMessageHandler? tokenEndpointHandler = null)
    {
        _store = store;
        _stateProtector = stateProtector;
        _config = config;
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

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        var priorPending = await store.GetAsync(
            userScope,
            GoogleClientFactory.OAuthPendingPackName,
            cancellationToken);
        var existingUser = await store.GetAsync(userScope, GoogleClientFactory.PackName, cancellationToken);
        var mergedCredentials = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var item in existingUser)
            mergedCredentials[item.Key] = item.Value;
        var pinnedClientStillCurrent =
            !priorPending.ContainsKey(GoogleClientFactory.OAuthPhaseKey) ||
            priorPending.TryGetValue(GoogleClientFactory.OAuthPendingClientIdKey, out var priorPinnedClientId) &&
            values.TryGetValue(GoogleClientFactory.ClientIdKey, out var configuredClientId) &&
            string.Equals(priorPinnedClientId, configuredClientId.Trim(), StringComparison.Ordinal);
        if (pinnedClientStillCurrent &&
            GoogleClientFactory.ResolveAuthorization(mergedCredentials, priorPending).State !=
                ExternalAuthorizationResolutionState.Ready &&
            GoogleClientFactory.TryGetReplayableAuthorizationChallenge(
                priorPending,
                out var replayUrl,
                out var replayState))
        {
            return new AuthChallenge(replayUrl, IsForm: false, State: replayState);
        }

        var flowId = GoogleClientFactory.CreateAuthorizationFlowId();

        var state = _stateProtector.Protect(user);

        string authUrl;
        try
        {
            authUrl = GoogleClientFactory.CreateAuthorizationUrl(values, redirectUri, state);
        }
        catch (InvalidOperationException ex)
        {
            return new AuthChallenge(UrlOrForm: "error:" + ex.Message, IsForm: true);
        }

        if (existingUser.Count > 0)
        {
            existingUser = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await store.SetAsync(userScope, GoogleClientFactory.PackName, existingUser, CancellationToken.None);
        }

        await store.SetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.OAuthPhaseKey] = GoogleClientFactory.OAuthPhaseChallengeIssued,
            [GoogleClientFactory.OAuthFlowIdKey] = flowId,
            [GoogleClientFactory.OAuthStateKey] = state,
            [GoogleClientFactory.OAuthAttemptFingerprintKey] =
                GoogleClientFactory.AuthorizationAttemptFingerprint(state),
            [GoogleClientFactory.OAuthAuthorizationUrlKey] = authUrl,
            [GoogleClientFactory.OAuthPendingClientIdKey] = values[GoogleClientFactory.ClientIdKey].Trim(),
            [GoogleClientFactory.OAuthPendingRedirectUriKey] = redirectUri.Trim(),
            [GoogleClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(GoogleClientFactory.OAuthPendingLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

        return new AuthChallenge(authUrl, IsForm: false, State: state);
    }

    public async Task<AuthResult> CompleteAuthAsync(OAuthCallback callback, CancellationToken cancellationToken = default)
    {
        if (!_stateProtector.TryUnprotect(callback.State, out var user))
            return new AuthResult(false, "invalid-state", "The authorization state is invalid or expired.");

        var userScope = PackConfigScopes.ForUser(new UserId(user.Value));
        var appValues = await _store.GetAsync(
            GoogleClientFactory.DefaultScope,
            GoogleClientFactory.PackName,
            cancellationToken);
        var pending = await _store.GetAsync(userScope, GoogleClientFactory.OAuthPendingPackName, cancellationToken);
        var existingUser = await _store.GetAsync(userScope, GoogleClientFactory.PackName, cancellationToken);

        if (GoogleClientFactory.IsKnownPendingExpired(pending))
        {
            await TerminalizePendingAsync(userScope, pending, "expired");
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (!pending.TryGetValue(GoogleClientFactory.OAuthStateKey, out var expectedState) || string.IsNullOrWhiteSpace(expectedState))
        {
            var callbackFingerprint = GoogleClientFactory.AuthorizationAttemptFingerprint(callback.State);
            var mergedCredentials = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            foreach (var item in existingUser)
                mergedCredentials[item.Key] = item.Value;
            if (existingUser.TryGetValue(GoogleClientFactory.OAuthCompletedFingerprintKey, out var completedFingerprint) &&
                GoogleClientFactory.SameAuthorizationAttempt(callbackFingerprint, completedFingerprint) &&
                GoogleClientFactory.HasUsableCredential(mergedCredentials))
                return new AuthResult(true);
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");
        }

        if (pending.TryGetValue(GoogleClientFactory.OAuthPhaseKey, out var phase) &&
            ((!string.Equals(phase, GoogleClientFactory.OAuthPhaseChallengeIssued, StringComparison.Ordinal) &&
              !string.Equals(phase, GoogleClientFactory.OAuthPhaseProcessing, StringComparison.Ordinal)) ||
             !pending.TryGetValue(GoogleClientFactory.OAuthFlowIdKey, out var phaseFlowId) ||
             !GoogleClientFactory.IsAuthorizationFlowId(phaseFlowId)))
            return new AuthResult(false, "no-pending", "No pending OAuth flow.");

        if (!string.Equals(expectedState, callback.State, StringComparison.Ordinal))
            return new AuthResult(false, "state-mismatch", "State did not match.");
        var attemptFingerprint = GoogleClientFactory.AuthorizationAttemptFingerprint(expectedState);
        var flowId = pending.TryGetValue(GoogleClientFactory.OAuthFlowIdKey, out var persistedFlowId) &&
                     GoogleClientFactory.IsAuthorizationFlowId(persistedFlowId)
            ? persistedFlowId
            : null;
        if (!pending.TryGetValue(GoogleClientFactory.OAuthAttemptFingerprintKey, out var persistedFingerprint) ||
            !GoogleClientFactory.SameAuthorizationAttempt(persistedFingerprint, attemptFingerprint))
            return new AuthResult(false, "state-mismatch", "State did not match.");
        var completedMatches = existingUser.TryGetValue(
                                   GoogleClientFactory.OAuthCompletedFingerprintKey,
                                   out var completedFingerprintForReplay) &&
                               GoogleClientFactory.SameAuthorizationAttempt(
                                   attemptFingerprint,
                                   completedFingerprintForReplay);
        var completedFlowMatches = flowId is null ||
                                   existingUser.TryGetValue(
                                       GoogleClientFactory.OAuthCompletedFlowIdKey,
                                       out var completedFlowForReplay) &&
                                   string.Equals(flowId, completedFlowForReplay, StringComparison.Ordinal);
        if (completedMatches && completedFlowMatches)
        {
            var mergedCredentials = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            foreach (var item in existingUser)
                mergedCredentials[item.Key] = item.Value;
            if (GoogleClientFactory.HasUsableCredential(mergedCredentials))
                return new AuthResult(true);
        }

        // Claim the flow before interpreting the provider response or crossing the token endpoint.
        // The owning Gmail grain serializes callbacks; the durable marker also keeps readiness false
        // while an exchange is in flight and makes every callback state one-shot across restarts.
        await SetPendingResultAsync(userScope, pending, "processing", attemptFingerprint, flowId);

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            var denied = string.Equals(callback.Error, "access_denied", StringComparison.OrdinalIgnoreCase);
            await SetPendingResultAsync(userScope, pending, denied ? "denied" : "provider-error", attemptFingerprint, flowId);
            return denied
                ? new AuthResult(false, "consent-denied", "Google consent was denied.")
                : new AuthResult(false, "provider-error", "Google authorization failed.");
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            await SetPendingResultAsync(userScope, pending, "incomplete", attemptFingerprint, flowId);
            return new AuthResult(false, "no-code", "The callback did not include an authorization code.");
        }

        var explicitPhase = pending.ContainsKey(GoogleClientFactory.OAuthPhaseKey);
        pending.TryGetValue(GoogleClientFactory.OAuthPendingClientIdKey, out var pinnedClientId);
        pending.TryGetValue(GoogleClientFactory.OAuthPendingRedirectUriKey, out var pinnedRedirectUri);
        appValues.TryGetValue(GoogleClientFactory.ClientIdKey, out var currentClientId);
        appValues.TryGetValue(GoogleClientFactory.ClientSecretKey, out var currentClientSecret);
        if (explicitPhase &&
            (string.IsNullOrWhiteSpace(pinnedClientId) ||
             string.IsNullOrWhiteSpace(pinnedRedirectUri) ||
             string.IsNullOrWhiteSpace(currentClientId) ||
             string.IsNullOrWhiteSpace(currentClientSecret) ||
             !string.Equals(pinnedClientId, currentClientId.Trim(), StringComparison.Ordinal)))
        {
            await SetPendingResultAsync(userScope, pending, "configuration-invalid", attemptFingerprint, flowId);
            return new AuthResult(false, "configuration-changed", "Google configuration changed. Start authorization again.");
        }

        var redirectUri = explicitPhase
            ? pinnedRedirectUri!
            : appValues.TryGetValue(GoogleClientFactory.RedirectUriKey, out var stored) && !string.IsNullOrWhiteSpace(stored)
                ? stored
                : (!string.IsNullOrWhiteSpace(callback.FallbackRedirectUri) ? callback.FallbackRedirectUri : null);
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            redirectUri = _config?["DigitalBrain:Google:RedirectUri"] ?? GoogleClientFactory.DefaultRedirectUri;
        }

        try
        {
            var exchangeValues = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
            if (explicitPhase)
            {
                exchangeValues[GoogleClientFactory.ClientIdKey] = pinnedClientId!;
                exchangeValues[GoogleClientFactory.RedirectUriKey] = pinnedRedirectUri!;
            }
            var tokenValues = await GoogleClientFactory.ExchangeAuthorizationCodeAsync(exchangeValues, callback.Code, redirectUri, _tokenEndpointHandler, cancellationToken);
            var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tokenValues)
            {
                userTokenValues[kv.Key] = kv.Value;
            }

            if (!userTokenValues.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var newRt) || string.IsNullOrWhiteSpace(newRt))
            {
                if (!explicitPhase &&
                    existingUser.TryGetValue(GoogleClientFactory.RefreshTokenKey, out var priorRt) &&
                    !string.IsNullOrWhiteSpace(priorRt))
                    userTokenValues[GoogleClientFactory.RefreshTokenKey] = priorRt;
                else
                {
                    await SetPendingResultAsync(userScope, pending, "missing-refresh-token", attemptFingerprint, flowId);
                    return new AuthResult(false, "exchange-failed", "Google did not return a reusable authorization.");
                }
            }

            // One pack replacement commits credentials and the completion witness together. If the
            // separate pending-pack cleanup is interrupted, readiness can still prove this exact flow.
            userTokenValues[GoogleClientFactory.OAuthCompletedFingerprintKey] = attemptFingerprint;
            if (flowId is not null)
                userTokenValues[GoogleClientFactory.OAuthCompletedFlowIdKey] = flowId;
            await _store.SetAsync(userScope, GoogleClientFactory.PackName, userTokenValues, cancellationToken);
            await BestEffortClearPendingAsync(userScope);

            return new AuthResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SetPendingResultAsync(userScope, pending, "cancelled", attemptFingerprint, flowId);
            throw;
        }
        catch (OperationCanceledException)
        {
            await SetPendingResultAsync(userScope, pending, "exchange-timeout", attemptFingerprint, flowId);
            return new AuthResult(false, "exchange-failed", "The authorization code exchange timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        values[GoogleClientFactory.OAuthPhaseKey] = processing
            ? GoogleClientFactory.OAuthPhaseProcessing
            : GoogleClientFactory.OAuthPhaseFailed;
        values[GoogleClientFactory.OAuthResultKey] = result;
        values[GoogleClientFactory.OAuthAttemptFingerprintKey] = attemptFingerprint;
        values[GoogleClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
            .Add(GoogleClientFactory.OAuthPendingLifetime)
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        if (flowId is not null)
            values[GoogleClientFactory.OAuthFlowIdKey] = flowId;
        if (processing)
            values[GoogleClientFactory.OAuthProcessingExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(GoogleClientFactory.OAuthProcessingLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        return _store.SetAsync(
            userScope,
            GoogleClientFactory.OAuthPendingPackName,
            values,
            CancellationToken.None);
    }

    private Task TerminalizePendingAsync(
        string userScope,
        IReadOnlyDictionary<string, string> pending,
        string result)
    {
        var terminal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [GoogleClientFactory.OAuthPhaseKey] = GoogleClientFactory.OAuthPhaseFailed,
            [GoogleClientFactory.OAuthResultKey] = result,
            [GoogleClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
                .Add(GoogleClientFactory.OAuthPendingLifetime)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture)
        };
        if (pending.TryGetValue(GoogleClientFactory.OAuthFlowIdKey, out var flowId) &&
            GoogleClientFactory.IsAuthorizationFlowId(flowId))
            terminal[GoogleClientFactory.OAuthFlowIdKey] = flowId;
        if (pending.TryGetValue(GoogleClientFactory.OAuthAttemptFingerprintKey, out var attempt) &&
            GoogleClientFactory.IsAuthorizationAttemptFingerprint(attempt))
            terminal[GoogleClientFactory.OAuthAttemptFingerprintKey] = attempt;
        return _store.SetAsync(
            userScope,
            GoogleClientFactory.OAuthPendingPackName,
            terminal,
            CancellationToken.None);
    }

    private async Task BestEffortClearPendingAsync(string userScope)
    {
        try
        {
            await _store.SetAsync(
                userScope,
                GoogleClientFactory.OAuthPendingPackName,
                new Dictionary<string, string>(),
                CancellationToken.None);
        }
        catch (Exception)
        {
            // The credential pack contains a matching completion witness, so the reconciler can
            // resume safely even if this independent cleanup write is interrupted.
        }
    }

    public async Task<ConnectionHealth> TestConnectionAsync(NeuronId user, CancellationToken cancellationToken = default)
    {
        try
        {
            var values = await GoogleClientFactory.GetMergedScopedValuesAsync(
                _store,
                new NeuronScope(new UserId(user.Value), ThreadId: null),
                cancellationToken);
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
        catch (Exception)
        {
            return new ConnectionHealth(Healthy: false, Detail: "Google connection probe failed.", Checked: DateTimeOffset.UtcNow);
        }
    }
}
