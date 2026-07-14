using System.Net.Http.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Kernel.Contracts.Runtime;
namespace DigitalBrain.Integrations.Google;

internal static class GoogleClientFactory
{
    public const string Provider = "google";
    public const string PackName = "google";
    public const string OAuthPendingPackName = "google-oauth-pending";
    public const string DefaultScope = "default";
    public const string DefaultCallbackPath = "/oauth/callback/google";
    public const string DefaultRedirectUri = "http://localhost:51014" + DefaultCallbackPath;
    public const string ClientIdKey = "client_id";
    public const string ClientSecretKey = "client_secret";
    public const string RefreshTokenKey = "refresh_token";
    public const string AccessTokenKey = "access_token";
    public const string RedirectUriKey = "redirect_uri";
    public const string OAuthStateKey = "oauth_state";
    public const string OAuthResultKey = "oauth_result";
    public const string OAuthAttemptFingerprintKey = "oauth_attempt_fingerprint";
    public const string OAuthCompletedFingerprintKey = "oauth_completed_fingerprint";
    public const string OAuthProcessingExpiresAtKey = "oauth_processing_expires_at";
    public const string OAuthCodeVerifierKey = "oauth_code_verifier";
    public const string OAuthPendingExpiresAtKey = "oauth_expires_at";
    public const string OAuthFlowIdKey = "oauth_flow_id";
    public const string OAuthCompletedFlowIdKey = "oauth_completed_flow_id";
    public const string OAuthPhaseKey = "oauth_phase";
    public const string OAuthAuthorizationUrlKey = "oauth_authorization_url";
    public const string OAuthStartTokenKey = "oauth_start_token";
    public const string OAuthStartTokenFingerprintKey = "oauth_start_token_fingerprint";
    public const string OAuthStartExpiresAtKey = "oauth_start_expires_at";
    public const string OAuthPendingClientIdKey = "oauth_client_id";
    public const string OAuthPendingRedirectUriKey = "oauth_redirect_uri";
    public const string OAuthPhaseLocalStart = "local-start";
    public const string OAuthPhaseChallengeIssued = "challenge-issued";
    public const string OAuthPhaseProcessing = "processing";
    public const string OAuthPhaseFailed = "failed";
    public static readonly TimeSpan OAuthPendingLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OAuthProcessingLifetime = TimeSpan.FromMinutes(3);
    public const string DefaultGmailScope = "https://www.googleapis.com/auth/gmail.readonly";
    public const string GmailSendScope = "https://www.googleapis.com/auth/gmail.send";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public static async Task<IReadOnlyDictionary<string, string>> GetMergedScopedValuesAsync(IIntegrationConfigStore store, NeuronScope scope, CancellationToken cancellationToken = default)
    {
        var appValues = await store.GetAsync(DefaultScope, PackName, cancellationToken).ConfigureAwait(false);
        var userValues = await store.GetAsync(IntegrationConfigScopes.ForUser(scope.UserId), PackName, cancellationToken).ConfigureAwait(false);
        var merged = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in userValues)
        {
            if (!IsAppOwnedConfigurationKey(key))
                merged[key] = value;
        }
        return merged;
    }
    public static string CreateAuthorizationUrl(IReadOnlyDictionary<string, string> values, string redirectUri, string state, params string[] additionalScopes)
    {
        var clientId = Required(values, ClientIdKey);
        var effectiveRedirect = string.IsNullOrWhiteSpace(redirectUri) ? Optional(values, RedirectUriKey, DefaultRedirectUri) : redirectUri;
        var scopes = new List<string> { DefaultGmailScope, GmailSendScope };
        if (additionalScopes.Length > 0)
        {
            scopes.AddRange(additionalScopes);
        }
        var scopeString = string.Join(" ", scopes);
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = effectiveRedirect,
            ["scope"] = scopeString,
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        return AuthEndpoint + "?" + QueryString(query);
    }
    public static string CreateOAuthStartUrl(string flowReference) =>
        OAuthCallbackPaths.CreateInternalStartPath(Provider, flowReference);
    public static async Task<IReadOnlyDictionary<string, string>> ExchangeAuthorizationCodeAsync(
        IReadOnlyDictionary<string, string> values,
        string code,
        string redirectUri,
        HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Google authorization callback did not include a code.");
        }
        var clientId = Required(values, ClientIdKey);
        var clientSecret = Required(values, ClientSecretKey);
        var effectiveRedirect = string.IsNullOrWhiteSpace(redirectUri) ? Optional(values, RedirectUriKey, DefaultRedirectUri) : redirectUri;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = effectiveRedirect
        };
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        using var content = new FormUrlEncodedContent(form);
        var response = await http.PostAsync(TokenEndpoint, content, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Google token exchange failed: " + responseBody);
        }
        var token = ParseTokenResponse(responseBody);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { [AccessTokenKey] = token.AccessToken ?? string.Empty, [RedirectUriKey] = effectiveRedirect };
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            result[RefreshTokenKey] = token.RefreshToken;
        }
        return result;
    }
    public static bool HasConnectedAppConfig(IReadOnlyDictionary<string, string> values) =>
        HasValue(values, ClientIdKey) && HasValue(values, ClientSecretKey);
    public static bool HasUsableCredential(IReadOnlyDictionary<string, string> values) =>
        HasValue(values, RefreshTokenKey) && HasConnectedAppConfig(values);
    public static string AuthorizationAttemptFingerprint(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))).ToLowerInvariant();
    }
    public static string CreateAuthorizationFlowId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    public static bool IsAuthorizationFlowId(string value)
    {
        try
        {
            return Convert.FromHexString(value).Length == 16;
        }
        catch (FormatException)
        {
            return false;
        }
    }
    internal static bool TryGetCurrentOAuthStartToken(IReadOnlyDictionary<string, string> pending, out string flowReference)
    {
        flowReference = string.Empty;
        if (!pending.TryGetValue(OAuthPhaseKey, out var phase) ||
            (!string.Equals(phase, OAuthPhaseLocalStart, StringComparison.Ordinal) &&
             !string.Equals(phase, OAuthPhaseChallengeIssued, StringComparison.Ordinal)) ||
            !pending.TryGetValue(OAuthStartTokenKey, out var candidate) ||
            !OAuthCallbackPaths.IsOpaqueFlowReference(candidate) ||
            !pending.TryGetValue(OAuthStartTokenFingerprintKey, out var fingerprint) ||
            !IsAuthorizationAttemptFingerprint(fingerprint) ||
            !SameAuthorizationAttempt(fingerprint, AuthorizationAttemptFingerprint(candidate)) ||
            !TryGetFutureUnixSeconds(pending, OAuthStartExpiresAtKey, out _))
            return false;
        flowReference = candidate;
        return true;
    }
    internal static bool IsCurrentOAuthStartToken(IReadOnlyDictionary<string, string> pending, string flowReference)
    {
        if (!OAuthCallbackPaths.IsOpaqueFlowReference(flowReference) || !TryGetCurrentOAuthStartToken(pending, out var current) ||
            current.Length != flowReference.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(flowReference));
    }
    public static bool IsAuthorizationReady(IReadOnlyDictionary<string, string> credentials, IReadOnlyDictionary<string, string> pending) =>
        ResolveAuthorization(credentials, pending).State == ExternalAuthorizationResolutionState.Ready;
    public static ExternalAuthorizationResolution ResolveAuthorization(IReadOnlyDictionary<string, string> credentials, IReadOnlyDictionary<string, string> pending)
    {
        var hasCredential = HasUsableCredential(credentials);
        var hasCompletedAttempt = credentials.TryGetValue(OAuthCompletedFingerprintKey, out var completedAttempt) &&
                                  IsAuthorizationAttemptFingerprint(completedAttempt);
        var hasCompletedFlow = credentials.TryGetValue(OAuthCompletedFlowIdKey, out var completedFlow) && IsAuthorizationFlowId(completedFlow);
        var hasPendingAttempt = pending.TryGetValue(OAuthAttemptFingerprintKey, out var pendingAttempt) && IsAuthorizationAttemptFingerprint(pendingAttempt);
        var hasPendingFlow = pending.TryGetValue(OAuthFlowIdKey, out var pendingFlow) && IsAuthorizationFlowId(pendingFlow);
        var explicitPhase = pending.ContainsKey(OAuthPhaseKey);
        if (hasCredential && hasCompletedAttempt && hasPendingAttempt && SameAuthorizationAttempt(pendingAttempt!, completedAttempt!) &&
            (explicitPhase
                ? hasPendingFlow && hasCompletedFlow && string.Equals(pendingFlow, completedFlow, StringComparison.Ordinal)
                : !hasPendingFlow || hasCompletedFlow && string.Equals(pendingFlow, completedFlow, StringComparison.Ordinal)))
            return new(ExternalAuthorizationResolutionState.Ready);
        if (pending.Count == 0)
            return hasCredential && hasCompletedAttempt && (!credentials.ContainsKey(OAuthCompletedFlowIdKey) || hasCompletedFlow)
                ? new(ExternalAuthorizationResolutionState.Ready)
                : new(ExternalAuthorizationResolutionState.Failed, "authorization-flow-missing");
        pending.TryGetValue(OAuthPhaseKey, out var phase);
        if (string.Equals(phase, OAuthPhaseLocalStart, StringComparison.Ordinal))
            return hasPendingFlow && TryGetCurrentOAuthStartToken(pending, out _)
                ? new(ExternalAuthorizationResolutionState.Waiting)
                : new(ExternalAuthorizationResolutionState.Failed, "authorization-start-invalid");
        if (string.Equals(phase, OAuthPhaseChallengeIssued, StringComparison.Ordinal))
            return TryGetReplayableAuthorizationChallenge(pending, out _, out _)
                ? new(ExternalAuthorizationResolutionState.Waiting)
                : new(ExternalAuthorizationResolutionState.Failed, "authorization-challenge-invalid");
        if (string.Equals(phase, OAuthPhaseProcessing, StringComparison.Ordinal))
            return hasPendingAttempt && hasPendingFlow && TryGetFutureUnixSeconds(pending, OAuthProcessingExpiresAtKey, out _)
                ? new(ExternalAuthorizationResolutionState.Waiting)
                : new(ExternalAuthorizationResolutionState.Failed, "authorization-exchange-interrupted");
        if (string.Equals(phase, OAuthPhaseFailed, StringComparison.Ordinal))
            return new(ExternalAuthorizationResolutionState.Failed, "authorization-failed");
        if (!string.IsNullOrWhiteSpace(phase))
            return new(ExternalAuthorizationResolutionState.Failed, "authorization-phase-invalid");
        if (!pending.TryGetValue(OAuthResultKey, out var result))
            return pending.TryGetValue(OAuthStateKey, out var state) && pending.TryGetValue(OAuthAttemptFingerprintKey, out var attempt) &&
                   !string.IsNullOrWhiteSpace(state) &&
                   SameAuthorizationAttempt(attempt, AuthorizationAttemptFingerprint(state)) &&
                   (!pending.ContainsKey(OAuthPendingExpiresAtKey) || TryGetFutureUnixSeconds(pending, OAuthPendingExpiresAtKey, out _))
                ? new(ExternalAuthorizationResolutionState.Waiting)
                : new(ExternalAuthorizationResolutionState.Failed, "authorization-state-invalid");
        if (!string.Equals(result, "processing", StringComparison.Ordinal))
            return new(ExternalAuthorizationResolutionState.Failed, "authorization-failed");
        return pending.TryGetValue(OAuthProcessingExpiresAtKey, out var processingExpiresAt) &&
               long.TryParse(processingExpiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt) &&
               expiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            ? new(ExternalAuthorizationResolutionState.Waiting)
            : new(ExternalAuthorizationResolutionState.Failed, "authorization-exchange-interrupted");
    }
    internal static bool TryGetReplayableAuthorizationChallenge(IReadOnlyDictionary<string, string> pending, out string authorizationUrl, out string state)
    {
        authorizationUrl = string.Empty;
        state = string.Empty;
        if (!pending.TryGetValue(OAuthPhaseKey, out var phase) ||
            (!string.Equals(phase, OAuthPhaseChallengeIssued, StringComparison.Ordinal) &&
             !string.Equals(phase, OAuthPhaseProcessing, StringComparison.Ordinal)) ||
            !pending.TryGetValue(OAuthFlowIdKey, out var flowId) || !IsAuthorizationFlowId(flowId) ||
            !pending.TryGetValue(OAuthStateKey, out var persistedState) || string.IsNullOrWhiteSpace(persistedState) ||
            !pending.TryGetValue(OAuthAttemptFingerprintKey, out var attempt) ||
            !SameAuthorizationAttempt(attempt, AuthorizationAttemptFingerprint(persistedState)) ||
            !pending.TryGetValue(OAuthAuthorizationUrlKey, out var persistedUrl) ||
            !IsAllowedAuthorizationUrl(persistedUrl) ||
            !TryGetFutureUnixSeconds(pending, string.Equals(phase, OAuthPhaseProcessing, StringComparison.Ordinal) ? OAuthProcessingExpiresAtKey : OAuthPendingExpiresAtKey, out _))
            return false;
        authorizationUrl = persistedUrl;
        state = persistedState;
        return true;
    }
    internal static bool IsKnownPendingExpired(IReadOnlyDictionary<string, string> pending)
    {
        pending.TryGetValue(OAuthPhaseKey, out var phase);
        var key = string.Equals(phase, OAuthPhaseProcessing, StringComparison.Ordinal)
            ? OAuthProcessingExpiresAtKey
            : string.Equals(phase, OAuthPhaseLocalStart, StringComparison.Ordinal) ? OAuthStartExpiresAtKey : OAuthPendingExpiresAtKey;
        return pending.TryGetValue(key, out var expiresAt) &&
               long.TryParse(expiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnixSeconds) &&
               expiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    internal static bool IsAuthorizationAttemptFingerprint(string value)
    {
        try
        {
            return Convert.FromHexString(value).Length == SHA256.HashSizeInBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
    public static bool IsAllowedAuthorizationUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 &&
        uri.Fragment.Length == 0 &&
        string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(uri.AbsolutePath, "/o/oauth2/v2/auth", StringComparison.Ordinal);
    private static bool IsAppOwnedConfigurationKey(string key) =>
        string.Equals(key, ClientIdKey, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, ClientSecretKey, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, RedirectUriKey, StringComparison.OrdinalIgnoreCase);
    private static bool TryGetFutureUnixSeconds(IReadOnlyDictionary<string, string> values, string key, out long expiresAtUnixSeconds)
    {
        expiresAtUnixSeconds = 0;
        return values.TryGetValue(key, out var expiresAt) &&
               long.TryParse(expiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out expiresAtUnixSeconds) &&
               expiresAtUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    public static bool SameAuthorizationAttempt(string left, string right)
    {
        try
        {
            var leftBytes = Convert.FromHexString(left);
            var rightBytes = Convert.FromHexString(right);
            return leftBytes.Length == SHA256.HashSizeInBytes && rightBytes.Length == SHA256.HashSizeInBytes &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
        throw new InvalidOperationException($"Google pack config is missing {key}. Complete \"Sign in with Google\" before using Gmail.");
    }
    private static string Optional(IReadOnlyDictionary<string, string> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
    private static bool HasValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    private static GoogleTokenResponse ParseTokenResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new GoogleTokenResponse(
                root.TryGetProperty("access_token", out var at) ? at.GetString() : null,
                root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
                root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 0);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Google token response was not valid JSON.", ex);
        }
    }
    private static string QueryString(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(kv =>
            Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
    private sealed record GoogleTokenResponse(string? AccessToken, string? RefreshToken, int ExpiresIn);
}
