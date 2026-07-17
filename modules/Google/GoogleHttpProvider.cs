using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Brain.Kernel.Connections;

namespace Brain.Modules.Google;

public sealed class GoogleHttpProvider(IHttpClientFactory httpClientFactory, GoogleProviderOptions options) : IConnectionProvider, IGmailProvider
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ProfileEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/profile";
    private const string MessagesEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages";
    private const string SendEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";
    private const string GmailScope = "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string BuildAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["scope"] = GmailScope,
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        return AuthorizationEndpoint + "?" + BuildQueryString(query);
    }

    public async Task<ConnectionToken> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["redirect_uri"] = options.RedirectUri
        };
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        var responseJson = await ConnectionHttp.SendThrowingAsync(client, request, ct);
        var root = JsonElement.Parse(responseJson);
        var accessToken = root.GetProperty("access_token").GetString() ?? "";
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement) ? refreshTokenElement.GetString() ?? "" : "";
        var expiresIn = root.TryGetProperty("expires_in", out var expiresInElement) ? expiresInElement.GetInt32() : 3600;
        return new ConnectionToken(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    public async Task<ProbeResult> ProbeAsync(ConnectionToken token, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await ConnectionHttp.ProbeGetAsync(client, request, ct);
    }

    public async Task<string> ListAsync(ConnectionToken token, int max, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{MessagesEndpoint}?maxResults={max}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await ConnectionHttp.SendThrowingAsync(client, request, ct);
    }

    public async Task<string> SendAsync(ConnectionToken token, string payloadJson, CancellationToken ct)
    {
        var (to, subject, body) = ParseSendPayload(payloadJson);
        var raw = Base64UrlEncode(BuildRfc2822Message(to, subject, body));
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { raw }, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var responseJson = await ConnectionHttp.SendThrowingAsync(client, request, ct);
        var root = JsonElement.Parse(responseJson);
        return root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
    }

    private static (string To, string Subject, string Body) ParseSendPayload(string payloadJson)
    {
        var root = JsonElement.Parse(payloadJson);
        return (root.GetProperty("to").GetString() ?? "", root.GetProperty("subject").GetString() ?? "", root.GetProperty("body").GetString() ?? "");
    }

    private static string BuildRfc2822Message(string to, string subject, string body)
    {
        var encodedSubject = Convert.ToBase64String(Encoding.UTF8.GetBytes(subject));
        return string.Join("\r\n",
            $"To: {to}",
            $"Subject: =?UTF-8?B?{encodedSubject}?=",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            body);
    }

    private static string Base64UrlEncode(string message) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(message)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string BuildQueryString(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
}
