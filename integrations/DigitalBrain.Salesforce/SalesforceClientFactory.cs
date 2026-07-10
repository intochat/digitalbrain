using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using Salesforce.Force;

namespace DigitalBrain.Salesforce;

public static class SalesforceClientFactory
{
    public const string PackName = "salesforce";
    public const string OAuthPendingPackName = "salesforce-oauth-pending";
    public const string DefaultScope = "default";
    public const string DefaultLoginUrl = "https://login.salesforce.com";
    public const string DefaultApiVersion = "v60.0";
    public const string DefaultCallbackPath = OAuthCallbackPaths.Salesforce;
    public const string DefaultRedirectUri = "http://localhost:8081" + DefaultCallbackPath;
    public const string DefaultOAuthScope = "api refresh_token";

    public const string ClientIdKey = "client_id";
    public const string ClientSecretKey = "client_secret";
    public const string LoginUrlKey = "login_url";
    public const string ApiVersionKey = "api_version";
    public const string AccessTokenKey = "access_token";
    public const string RefreshTokenKey = "refresh_token";
    public const string InstanceUrlKey = "instance_url";
    public const string RedirectUriKey = "redirect_uri";
    public const string OAuthStateKey = "oauth_state";
    public const string OAuthScopeKey = "oauth_scope";
    public const string OAuthCodeVerifierKey = "oauth_code_verifier";
    public const string AuthenticationFailureMessage =
        "Salesforce authentication failed. Reconnect Salesforce and try again.";
    public const string MissingConnectedAppConfigMessage =
        "Salesforce OAuth is not configured. Configure the Connected App Client ID and Client Secret in Aspire parameters (salesforce-client-id and salesforce-client-secret) or save them in the Salesforce credentials form, then try Login via Salesforce again.";

    public static async Task<IReadOnlyDictionary<string, string>> GetMergedScopedValuesAsync(
        IPackConfigStore store,
        NeuronScope scope,
        CancellationToken cancellationToken = default)
    {
        var appValues = await store.GetAsync(PackConfigScopes.App, PackName, cancellationToken).ConfigureAwait(false);
        var userValues = await store.GetAsync(PackConfigScopes.ForUser(scope.UserId), PackName, cancellationToken).ConfigureAwait(false);

        var merged = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in userValues)
        {
            merged[key] = value;
        }

        return merged;
    }

    public static async Task<ForceClient> CreateForceClientAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        var apiVersion = NormalizeApiVersion(Optional(values, ApiVersionKey, DefaultApiVersion));

        if (HasOAuthCredential(values))
        {
            return await CreateOAuthForceClientAsync(values, apiVersion, cancellationToken).ConfigureAwait(false);
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

    public static string CreateAuthorizationUrl(
        IReadOnlyDictionary<string, string> values,
        string redirectUri,
        string state,
        string? codeChallenge = null)
    {
        RequireConnectedAppConfig(values);
        var clientId = Required(values, ClientIdKey);
        var loginUrl = Optional(values, LoginUrlKey, DefaultLoginUrl);
        var scope = Optional(values, OAuthScopeKey, DefaultOAuthScope);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = string.IsNullOrWhiteSpace(redirectUri) ? DefaultRedirectUri : redirectUri,
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
        var loginUrl = Optional(values, LoginUrlKey, DefaultLoginUrl);
        var effectiveRedirectUri = string.IsNullOrWhiteSpace(redirectUri)
            ? Optional(values, RedirectUriKey, DefaultRedirectUri)
            : redirectUri;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = effectiveRedirectUri
        };
        if (values.TryGetValue(OAuthCodeVerifierKey, out var codeVerifier) &&
            !string.IsNullOrWhiteSpace(codeVerifier))
        {
            form["code_verifier"] = codeVerifier.Trim();
        }

        var token = await RequestTokenAsync(TokenEndpoint(loginUrl), form, tokenEndpointHandler, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.InstanceUrl))
        {
            throw new InvalidOperationException(
                "Salesforce authorization response did not include access_token and instance_url.");
        }

        var result = new Dictionary<string, string>
        {
            [AccessTokenKey] = token.AccessToken,
            [InstanceUrlKey] = token.InstanceUrl,
            [RedirectUriKey] = effectiveRedirectUri
        };
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
        var value = NormalizeLoginUrlOrEndpoint(loginUrlOrEndpoint);

        if (value.EndsWith("/services/oauth2/authorize", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return value.TrimEnd('/') + "/services/oauth2/authorize";
    }

    public static string TokenEndpoint(string loginUrlOrEndpoint)
    {
        var value = NormalizeLoginUrlOrEndpoint(loginUrlOrEndpoint);

        if (value.EndsWith("/services/oauth2/token", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return value.TrimEnd('/') + "/services/oauth2/token";
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
        var trimmed = value.Trim();
        return trimmed.StartsWith('v') ? trimmed : "v" + trimmed;
    }

    private static string NormalizeLoginUrlOrEndpoint(string loginUrlOrEndpoint)
    {
        if (string.IsNullOrWhiteSpace(loginUrlOrEndpoint))
        {
            return DefaultLoginUrl;
        }

        var value = loginUrlOrEndpoint.Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + value;
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            return DefaultLoginUrl.TrimEnd('/') + value;
        }

        return "https://" + value;
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

    private static string Optional(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback = "") =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

    private static async Task<ForceClient> CreateOAuthForceClientAsync(
        IReadOnlyDictionary<string, string> values,
        string apiVersion,
        CancellationToken cancellationToken = default)
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

            var instanceUrl = string.IsNullOrWhiteSpace(token.InstanceUrl)
                ? Optional(values, InstanceUrlKey)
                : token.InstanceUrl;
            if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(instanceUrl))
            {
                throw new InvalidOperationException(
                    "Salesforce refresh-token response did not include access_token and instance_url.");
            }

            return new ForceClient(instanceUrl, token.AccessToken, apiVersion);
        }

        if (HasValue(values, AccessTokenKey) && HasValue(values, InstanceUrlKey))
        {
            return new ForceClient(Required(values, InstanceUrlKey), Required(values, AccessTokenKey), apiVersion);
        }

        if (HasValue(values, RefreshTokenKey))
        {
            RequireConnectedAppConfig(values);
        }

        return new ForceClient(Required(values, InstanceUrlKey), Required(values, AccessTokenKey), apiVersion);
    }

    private static void RequireConnectedAppConfig(IReadOnlyDictionary<string, string> values)
    {
        if (!HasConnectedAppConfig(values))
        {
            throw new InvalidOperationException(MissingConnectedAppConfigMessage);
        }
    }

    private static async Task<SalesforceTokenResponse> RequestTokenAsync(
        string tokenEndpoint,
        IReadOnlyDictionary<string, string> form,
        HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
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
            // Fall through to sanitized raw body.
        }

        var trimmed = responseBody.Trim();
        return string.IsNullOrWhiteSpace(trimmed)
            ? "Salesforce returned no error details."
            : "Salesforce returned: " + trimmed;
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    private static bool HasValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static string QueryString(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(kv =>
            Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record SalesforceTokenResponse(
        string AccessToken,
        string InstanceUrl,
        string RefreshToken,
        string IssuedAt,
        string Scope);
}
