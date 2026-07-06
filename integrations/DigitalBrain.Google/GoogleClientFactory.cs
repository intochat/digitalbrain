using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigitalBrain.Google;

public static class GoogleClientFactory
{
    public const string PackName = "google";
    public const string OAuthPendingPackName = "google-oauth-pending";
    public const string DefaultScope = "default";
    public const string DefaultCallbackPath = "/google-callback";

    public const string ClientIdKey = "client_id";
    public const string ClientSecretKey = "client_secret";
    public const string RefreshTokenKey = "refresh_token";
    public const string AccessTokenKey = "access_token";
    public const string RedirectUriKey = "redirect_uri";
    public const string OAuthStateKey = "oauth_state";
    public const string OAuthCodeVerifierKey = "oauth_code_verifier";

    public const string DefaultGmailScope = "https://www.googleapis.com/auth/gmail.readonly";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    public static async Task<IReadOnlyDictionary<string, string>> GetMergedScopedValuesAsync(
        IPackConfigStore store,
        NeuronScope scope)
    {
        var appValues = await store.GetAsync(DefaultScope, PackName).ConfigureAwait(false);
        var userValues = await store.GetAsync(PackConfigScopes.ForUser(scope.UserId), PackName).ConfigureAwait(false);

        var merged = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in userValues)
            merged[key] = value;

        return merged;
    }

    public static string CreateAuthorizationUrl(
        IReadOnlyDictionary<string, string> values,
        string redirectUri,
        string state,
        params string[] additionalScopes)
    {
        var clientId = Required(values, ClientIdKey);
        var effectiveRedirect = string.IsNullOrWhiteSpace(redirectUri)
            ? Optional(values, RedirectUriKey, "http://localhost:51014/google-callback")
            : redirectUri;

        var scopes = new List<string> { DefaultGmailScope };
        if (additionalScopes.Length > 0)
            scopes.AddRange(additionalScopes);

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

    public static async Task<IReadOnlyDictionary<string, string>> ExchangeAuthorizationCodeAsync(
        IReadOnlyDictionary<string, string> values,
        string code,
        string redirectUri,
        HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Google authorization callback did not include a code.");

        var clientId = Required(values, ClientIdKey);
        var clientSecret = Required(values, ClientSecretKey);
        var effectiveRedirect = string.IsNullOrWhiteSpace(redirectUri)
            ? Optional(values, RedirectUriKey, "http://localhost:51014/google-callback")
            : redirectUri;

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

        var response = await http.PostAsync(TokenEndpoint, content).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Google token exchange failed: " + responseBody);

        var token = ParseTokenResponse(responseBody);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AccessTokenKey] = token.AccessToken ?? string.Empty,
            [RedirectUriKey] = effectiveRedirect
        };

        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            result[RefreshTokenKey] = token.RefreshToken;

        return result;
    }

    public static bool HasConnectedAppConfig(IReadOnlyDictionary<string, string> values) =>
        HasValue(values, ClientIdKey) && HasValue(values, ClientSecretKey);

    public static bool HasUsableCredential(IReadOnlyDictionary<string, string> values) =>
        HasValue(values, RefreshTokenKey) && HasConnectedAppConfig(values);

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        throw new InvalidOperationException(
            $"Google pack config is missing {key}. Complete \"Sign in with Google\" before using Gmail.");
    }

    private static string Optional(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback = "") =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

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