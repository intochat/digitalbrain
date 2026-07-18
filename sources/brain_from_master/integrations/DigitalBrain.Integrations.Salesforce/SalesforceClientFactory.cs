using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Kernel.Contracts.Runtime;
using Salesforce.Force;
namespace DigitalBrain.Integrations.Salesforce;

internal static class SalesforceClientFactory
{
    public const string Provider = "salesforce";
    public const string PackName = "salesforce";
    public const string OAuthPendingPackName = "salesforce-oauth-pending";
    public const string DefaultScope = "default";
    public const string DefaultLoginUrl = "https://login.salesforce.com";
    public const string DefaultApiVersion = "v60.0";
    public const string DefaultCallbackPath = "/oauth/callback/salesforce";
    public const string DefaultRedirectUri = "http://localhost:51014" + DefaultCallbackPath;
    public const string DefaultOAuthScope = "api refresh_token";
    public const string ClientIdKey = "client_id";
    public const string ClientSecretKey = "client_secret";
    public const string LoginUrlKey = "login_url";
    public const string ApiVersionKey = "api_version";
    public const string AccessTokenKey = "access_token";
    public const string RefreshTokenKey = "refresh_token";
    public const string InstanceUrlKey = "instance_url";
    public const string IdentityUrlKey = "identity_url";
    public const string RedirectUriKey = "redirect_uri";
    public const string OAuthStateKey = "oauth_state";
    public const string OAuthScopeKey = "oauth_scope";
    public const string OAuthCodeVerifierKey = "oauth_code_verifier";
    public const string OAuthPendingClientIdKey = "oauth_client_id";
    public const string OAuthPendingLoginUrlKey = "oauth_login_url";
    public const string OAuthPendingRedirectUriKey = "oauth_redirect_uri";
    public const string OAuthPendingExpiresAtKey = "oauth_expires_at";
    public const string OAuthResultKey = "oauth_result";
    public const string OAuthAttemptFingerprintKey = "oauth_attempt_fingerprint";
    public const string OAuthCompletedFingerprintKey = "oauth_completed_fingerprint";
    public const string OAuthCompletedExpiresAtKey = "oauth_completed_expires_at";
    public const string OAuthProcessingExpiresAtKey = "oauth_processing_expires_at";
    public const string OAuthFlowIdKey = "oauth_flow_id";
    public const string OAuthCompletedFlowIdKey = "oauth_completed_flow_id";
    public const string OAuthPhaseKey = "oauth_phase";
    public const string OAuthAuthorizationUrlKey = "oauth_authorization_url";
    public const string OAuthStartTokenKey = "oauth_start_token";
    public const string OAuthStartTokenFingerprintKey = "oauth_start_token_fingerprint";
    public const string OAuthStartExpiresAtKey = "oauth_start_expires_at";
    public const string OAuthPhaseLocalStart = "local-start";
    public const string OAuthPhaseChallengeIssued = "challenge-issued";
    public const string OAuthPhaseProcessing = "processing";
    public const string OAuthPhaseFailed = "failed";
    public static readonly TimeSpan OAuthPendingLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OAuthProcessingLifetime = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan OAuthCompletedWitnessLifetime = TimeSpan.FromHours(1);
    public const string AuthenticationFailureMessage =
        "Salesforce authentication failed. Reconnect Salesforce and try again.";
    public const string MissingConnectedAppConfigMessage =
        "Salesforce OAuth is not configured. Configure the Connected App Client ID and Client Secret in Aspire parameters (salesforce-client-id and salesforce-client-secret) or save them in the Salesforce credentials form, then try Login via Salesforce again.";
    public static async Task<IReadOnlyDictionary<string, string>> GetMergedScopedValuesAsync(IIntegrationConfigStore store, NeuronScope scope, CancellationToken cancellationToken = default)
    {
        var appValues = await store.GetAsync(IntegrationConfigScopes.App, PackName, cancellationToken).ConfigureAwait(false);
        var userValues = await store.GetAsync(IntegrationConfigScopes.ForUser(scope.UserId), PackName, cancellationToken).ConfigureAwait(false);
        var merged = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in userValues)
        {
            if (!AppOwnedKeys.Contains(key))
            {
                merged[key] = value;
            }
        }
        return merged;
    }
    public static async Task<ForceClient> CreateForceClientAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
        => (await CreateSessionAsync(values, cancellationToken).ConfigureAwait(false)).Client;
    public static async Task<SalesforceClientSession> CreateSessionAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        var apiVersion = NormalizeApiVersion(Optional(values, ApiVersionKey, DefaultApiVersion));
        if (HasOAuthCredential(values))
        {
            return await CreateOAuthSessionAsync(values, apiVersion, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Salesforce is not connected for this principal.");
    }
    public static bool HasUsableCredential(IReadOnlyDictionary<string, string> values) =>
        HasOAuthCredential(values);
    public static bool HasOAuthCredential(IReadOnlyDictionary<string, string> values) =>
        (HasValue(values, AccessTokenKey) && HasValue(values, InstanceUrlKey)) ||
        (HasValue(values, RefreshTokenKey) && HasValue(values, ClientIdKey) && HasValue(values, ClientSecretKey));
    public static bool HasConnectedAppConfig(IReadOnlyDictionary<string, string> values) =>
        HasValue(values, ClientIdKey) && HasValue(values, ClientSecretKey);
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
    public static ExternalAuthorizationResolution ResolveAuthorization(IReadOnlyDictionary<string, string> credentials, IReadOnlyDictionary<string, string> pending)
    {
        var hasCredential = HasUsableCredential(credentials);
        var hasCompletedAttempt = credentials.TryGetValue(OAuthCompletedFingerprintKey, out var completedAttempt) &&
                                  IsAuthorizationAttemptFingerprint(completedAttempt) &&
                                  TryGetFutureUnixSeconds(credentials, OAuthCompletedExpiresAtKey, out _);
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
        {
            if (!hasPendingFlow || !pending.TryGetValue(OAuthStartTokenKey, out var startToken) || string.IsNullOrWhiteSpace(startToken) ||
                !pending.TryGetValue(OAuthStartTokenFingerprintKey, out var startFingerprint) ||
                !SameAuthorizationAttempt(startFingerprint, AuthorizationAttemptFingerprint(startToken)) ||
                !TryGetFutureUnixSeconds(pending, OAuthStartExpiresAtKey, out _))
            {
                return new(ExternalAuthorizationResolutionState.Failed, "authorization-start-invalid");
            }
            return new(ExternalAuthorizationResolutionState.Waiting);
        }
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
    internal static bool IsProviderAuthorizationPhase(IReadOnlyDictionary<string, string> pending) =>
        pending.TryGetValue(OAuthPhaseKey, out var phase) &&
        (string.Equals(phase, OAuthPhaseChallengeIssued, StringComparison.Ordinal) ||
         string.Equals(phase, OAuthPhaseProcessing, StringComparison.Ordinal) ||
         string.Equals(phase, OAuthPhaseFailed, StringComparison.Ordinal));
    internal static bool TryGetReplayableAuthorizationChallenge(IReadOnlyDictionary<string, string> pending, out string authorizationUrl, out string state)
    {
        authorizationUrl = string.Empty;
        state = string.Empty;
        if (!pending.TryGetValue(OAuthPhaseKey, out var phase) || !string.Equals(phase, OAuthPhaseChallengeIssued, StringComparison.Ordinal) ||
            !pending.TryGetValue(OAuthFlowIdKey, out var flowId) || !IsAuthorizationFlowId(flowId) ||
            !pending.TryGetValue(OAuthStateKey, out var persistedState) || string.IsNullOrWhiteSpace(persistedState) ||
            !pending.TryGetValue(OAuthAttemptFingerprintKey, out var attempt) ||
            !SameAuthorizationAttempt(attempt, AuthorizationAttemptFingerprint(persistedState)) ||
            !pending.TryGetValue(OAuthAuthorizationUrlKey, out var persistedAuthorizationUrl) ||
            !IsAllowedAuthorizationUrl(persistedAuthorizationUrl) ||
            !TryGetFutureUnixSeconds(pending, OAuthPendingExpiresAtKey, out _))
        {
            authorizationUrl = string.Empty;
            state = string.Empty;
            return false;
        }
        state = persistedState;
        authorizationUrl = persistedAuthorizationUrl;
        return true;
    }
    internal static bool IsKnownPendingExpired(IReadOnlyDictionary<string, string> pending)
    {
        if (pending.Count == 0)
            return false;
        var key = pending.TryGetValue(OAuthPhaseKey, out var phase)
            ? phase switch
            {
                OAuthPhaseLocalStart => OAuthStartExpiresAtKey,
                OAuthPhaseChallengeIssued => OAuthPendingExpiresAtKey,
                OAuthPhaseProcessing => OAuthProcessingExpiresAtKey,
                OAuthPhaseFailed => OAuthPendingExpiresAtKey,
                _ => null
            }
            : pending.ContainsKey(OAuthPendingExpiresAtKey) ? OAuthPendingExpiresAtKey : null;
        return key is not null && pending.TryGetValue(key, out var expiresAt) &&
               long.TryParse(expiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnixSeconds) &&
               expiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    private static bool TryGetFutureUnixSeconds(IReadOnlyDictionary<string, string> values, string key, out long expiresAtUnixSeconds)
    {
        expiresAtUnixSeconds = 0;
        return values.TryGetValue(key, out var expiresAt) &&
               long.TryParse(expiresAt, NumberStyles.None, CultureInfo.InvariantCulture, out expiresAtUnixSeconds) &&
               expiresAtUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
    public static bool TryValidateAppConfig(IReadOnlyDictionary<string, string> values, out string? invalidKey, out string? message)
    {
        foreach (var key in new[] { ClientIdKey, ClientSecretKey })
        {
            if (!HasValue(values, key))
            {
                invalidKey = key;
                message = $"Missing {key}";
                return false;
            }
        }
        if (!TryNormalizeLoginUrl(Optional(values, LoginUrlKey, DefaultLoginUrl), out _))
        {
            invalidKey = LoginUrlKey;
            message = "Salesforce login_url must be an approved Salesforce HTTPS origin.";
            return false;
        }
        if (!TryNormalizeRedirectUri(Optional(values, RedirectUriKey, DefaultRedirectUri), out _))
        {
            invalidKey = RedirectUriKey;
            message = $"Salesforce redirect_uri must use {DefaultCallbackPath}; HTTP is allowed only for loopback development.";
            return false;
        }
        if (!TryNormalizeApiVersion(Optional(values, ApiVersionKey, DefaultApiVersion), out _))
        {
            invalidKey = ApiVersionKey;
            message = "Salesforce api_version must use the vNN.N format.";
            return false;
        }
        invalidKey = null;
        message = null;
        return true;
    }
    public static string ResolveLoginUrl(IReadOnlyDictionary<string, string> values) =>
        NormalizeLoginUrl(Optional(values, LoginUrlKey, DefaultLoginUrl));
    public static string ResolveRedirectUri(IReadOnlyDictionary<string, string> values) =>
        NormalizeRedirectUri(Optional(values, RedirectUriKey, DefaultRedirectUri));
    public static string CreateOAuthStartUrl(string flowReference) =>
        OAuthCallbackPaths.CreateInternalStartPath(Provider, flowReference);
    public static string CreateOAuthStartUrl(IReadOnlyDictionary<string, string> values, string flowReference)
    {
        ArgumentNullException.ThrowIfNull(values);
        return CreateOAuthStartUrl(flowReference);
    }
    public static bool IsAllowedAuthorizationUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 &&
        uri.Fragment.Length == 0 &&
        (uri.Host.EndsWith(".salesforce.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".site.com", StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(uri.AbsolutePath, "/services/oauth2/authorize", StringComparison.Ordinal);
    public static string CreateAuthorizationUrl(IReadOnlyDictionary<string, string> values, string redirectUri, string state, string? codeChallenge = null)
    {
        RequireConnectedAppConfig(values);
        var clientId = Required(values, ClientIdKey);
        var loginUrl = ResolveLoginUrl(values);
        var scope = Optional(values, OAuthScopeKey, DefaultOAuthScope);
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = string.IsNullOrWhiteSpace(redirectUri) ? ResolveRedirectUri(values) : NormalizeRedirectUri(redirectUri),
            ["scope"] = scope,
            ["state"] = state
        };
        if (!string.IsNullOrWhiteSpace(codeChallenge))
        {
            query["code_challenge"] = codeChallenge;
            query["code_challenge_method"] = "S256";
        }
        return AuthorizationEndpoint(loginUrl) + "?" + QueryString(query);
    }
    public static async Task<IReadOnlyDictionary<string, string>> ExchangeAuthorizationCodeAsync(
        IReadOnlyDictionary<string, string> values,
        string code,
        string redirectUri,
        HttpMessageHandler? tokenEndpointHandler = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Salesforce authorization callback did not include a code.");
        }
        var clientId = Required(values, ClientIdKey);
        var clientSecret = Required(values, ClientSecretKey);
        var loginUrl = ResolveLoginUrl(values);
        var effectiveRedirectUri = string.IsNullOrWhiteSpace(redirectUri) ? ResolveRedirectUri(values) : NormalizeRedirectUri(redirectUri);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = effectiveRedirectUri
        };
        if (values.TryGetValue(OAuthCodeVerifierKey, out var codeVerifier) && !string.IsNullOrWhiteSpace(codeVerifier))
        {
            form["code_verifier"] = codeVerifier.Trim();
        }
        var token = await RequestTokenAsync(TokenEndpoint(loginUrl), form, tokenEndpointHandler, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.InstanceUrl))
        {
            throw new InvalidOperationException("Salesforce authorization response did not include access_token and instance_url.");
        }
        var result = new Dictionary<string, string> { [AccessTokenKey] = token.AccessToken, [InstanceUrlKey] = token.InstanceUrl };
        if (!string.IsNullOrWhiteSpace(token.IdentityUrl))
        {
            result[IdentityUrlKey] = token.IdentityUrl;
        }
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            result[RefreshTokenKey] = token.RefreshToken;
        }
        if (!string.IsNullOrWhiteSpace(token.IssuedAt))
        {
            result["issued_at"] = token.IssuedAt;
        }
        if (!string.IsNullOrWhiteSpace(token.Scope))
        {
            result[OAuthScopeKey] = token.Scope;
        }
        return result;
    }
    public static string AuthorizationEndpoint(string loginUrlOrEndpoint)
    {
        var value = NormalizeLoginUrl(loginUrlOrEndpoint);
        return value + "/services/oauth2/authorize";
    }
    public static string TokenEndpoint(string loginUrlOrEndpoint)
    {
        var value = NormalizeLoginUrl(loginUrlOrEndpoint);
        return value + "/services/oauth2/token";
    }
    public static string CreatePkceCodeVerifier() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string CreatePkceCodeChallenge(string codeVerifier)
    {
        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            throw new ArgumentException("PKCE code verifier is required.", nameof(codeVerifier));
        }
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier.Trim())));
    }
    private static string NormalizeApiVersion(string value)
    {
        if (TryNormalizeApiVersion(value, out var normalized)) return normalized;
        throw new InvalidOperationException("Salesforce api_version must use the vNN.N format.");
    }
    private static bool TryNormalizeApiVersion(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var version = value.Trim();
        if (version.StartsWith('v') || version.StartsWith('V')) version = version[1..];
        var parts = version.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var major) || major <= 0 || !int.TryParse(parts[1], out var minor) || minor < 0)
        {
            return false;
        }
        normalized = $"v{major}.{minor}";
        return true;
    }
    private static string NormalizeLoginUrl(string value)
    {
        if (TryNormalizeLoginUrl(value, out var normalized)) return normalized;
        throw new InvalidOperationException("Salesforce login_url must be an approved Salesforce HTTPS origin.");
    }
    private static bool TryNormalizeLoginUrl(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal)) candidate = "https://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !IsAllowedLoginHost(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath is not ("" or "/"))
        {
            return false;
        }
        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
    private static bool IsAllowedLoginHost(string host) =>
        string.Equals(host, "login.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "test.salesforce.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".salesforce.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".site.com", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeRedirectUri(string value)
    {
        if (TryNormalizeRedirectUri(value, out var normalized)) return normalized;
        throw new InvalidOperationException($"Salesforce redirect_uri must use {DefaultCallbackPath}; HTTP is allowed only for loopback development.");
    }
    private static bool TryNormalizeRedirectUri(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.AbsolutePath, DefaultCallbackPath, StringComparison.Ordinal) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            return false;
        }
        normalized = uri.AbsoluteUri;
        return true;
    }
    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
        throw new InvalidOperationException(
            $"Salesforce pack config (scope '{DefaultScope}', pack '{PackName}') is missing {key}. " +
            "Complete the Salesforce credentials prompt before using Salesforce CRM neurons.");
    }
    private static string Optional(IReadOnlyDictionary<string, string> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
    private static async Task<SalesforceClientSession> CreateOAuthSessionAsync(IReadOnlyDictionary<string, string> values, string apiVersion, CancellationToken cancellationToken = default)
    {
        if (HasValue(values, RefreshTokenKey) && HasConnectedAppConfig(values))
        {
            var clientId = Required(values, ClientIdKey);
            var clientSecret = Required(values, ClientSecretKey);
            var loginUrl = Optional(values, LoginUrlKey, DefaultLoginUrl);
            var token = await RequestTokenAsync(TokenEndpoint(loginUrl), new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = Required(values, RefreshTokenKey),
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var instanceUrl = string.IsNullOrWhiteSpace(token.InstanceUrl) ? Optional(values, InstanceUrlKey) : token.InstanceUrl;
            if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(instanceUrl))
            {
                throw new InvalidOperationException("Salesforce refresh-token response did not include access_token and instance_url.");
            }
            return new SalesforceClientSession(
                new ForceClient(instanceUrl, token.AccessToken, apiVersion),
                string.IsNullOrWhiteSpace(token.IdentityUrl) ? Optional(values, IdentityUrlKey) : token.IdentityUrl);
        }
        if (HasValue(values, AccessTokenKey) && HasValue(values, InstanceUrlKey))
        {
            return new SalesforceClientSession(new ForceClient(Required(values, InstanceUrlKey), Required(values, AccessTokenKey), apiVersion), Optional(values, IdentityUrlKey));
        }
        if (HasValue(values, RefreshTokenKey))
        {
            RequireConnectedAppConfig(values);
        }
        return new SalesforceClientSession(new ForceClient(Required(values, InstanceUrlKey), Required(values, AccessTokenKey), apiVersion), Optional(values, IdentityUrlKey));
    }
    private static void RequireConnectedAppConfig(IReadOnlyDictionary<string, string> values)
    {
        if (!HasConnectedAppConfig(values))
        {
            throw new InvalidOperationException(MissingConnectedAppConfigMessage);
        }
    }
    private static async Task<SalesforceTokenResponse> RequestTokenAsync(string tokenEndpoint, IReadOnlyDictionary<string, string> form, HttpMessageHandler? handler = null, CancellationToken cancellationToken = default)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        using var content = new FormUrlEncodedContent(form);
        HttpResponseMessage response;
        string responseBody;
        try
        {
            response = await http.PostAsync(tokenEndpoint, content, cancellationToken).ConfigureAwait(false);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(AuthenticationFailureMessage + " " + ex.Message, ex);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(AuthenticationFailureMessage + " " + SalesforceErrorDetails(responseBody));
            }
            return ParseTokenResponse(responseBody);
        }
    }
    private static SalesforceTokenResponse ParseTokenResponse(string responseBody)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Salesforce authentication response was not valid JSON.", ex);
        }
        return new SalesforceTokenResponse(
            GetString(root, "access_token"),
            GetString(root, "instance_url"),
            GetString(root, "id"),
            GetString(root, "refresh_token"),
            GetString(root, "issued_at"),
            GetString(root, "scope"));
    }
    private static string SalesforceErrorDetails(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var error = GetString(root, "error");
            var description = GetString(root, "error_description");
            if (!string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(description))
            {
                return $"Salesforce returned {error}: {description}";
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                return $"Salesforce returned {error}.";
            }
            if (!string.IsNullOrWhiteSpace(description))
            {
                return $"Salesforce returned: {description}";
            }
        }
        catch (JsonException)
        {
        }
        var trimmed = responseBody.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "Salesforce returned no error details." : "Salesforce returned: " + trimmed;
    }
    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;
    private static bool HasValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
    private static readonly HashSet<string> AppOwnedKeys = new(StringComparer.OrdinalIgnoreCase) { ClientIdKey, ClientSecretKey, LoginUrlKey, ApiVersionKey, RedirectUriKey };
    private static string QueryString(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(kv =>
            Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed record SalesforceTokenResponse(string AccessToken, string InstanceUrl, string IdentityUrl, string RefreshToken, string IssuedAt, string Scope);
}
internal sealed record SalesforceClientSession(ForceClient Client, string? IdentityUrl);
