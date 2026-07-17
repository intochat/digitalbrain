using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Brain.Modules.Connections;

namespace Brain.Modules.Salesforce;

public sealed class SalesforceHttpProvider(IHttpClientFactory httpClientFactory, SalesforceProviderOptions options) : IConnectionProvider, ISalesforceProvider
{
    private const string OAuthScope = "api refresh_token";

    public string BuildAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["scope"] = OAuthScope,
            ["state"] = state
        };
        return $"{options.LoginHost}/services/oauth2/authorize?{BuildQueryString(query)}";
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
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.LoginHost}/services/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        var responseJson = await ConnectionHttp.SendThrowingAsync(client, request, ct);
        var root = JsonElement.Parse(responseJson);
        var accessToken = root.GetProperty("access_token").GetString() ?? "";
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshTokenElement) ? refreshTokenElement.GetString() ?? "" : "";
        var instanceUrl = root.TryGetProperty("instance_url", out var instanceUrlElement) ? instanceUrlElement.GetString() : null;
        return new ConnectionToken(accessToken, refreshToken, DateTimeOffset.UtcNow.AddHours(2), instanceUrl);
    }

    public async Task<ProbeResult> ProbeAsync(ConnectionToken token, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ResolveInstanceUrl(token)}/services/data/{options.ApiVersion}/sobjects");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await ConnectionHttp.ProbeGetAsync(client, request, ct);
    }

    public async Task<string> QueryAsync(ConnectionToken token, string soql, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ResolveInstanceUrl(token)}/services/data/{options.ApiVersion}/query?q={Uri.EscapeDataString(soql)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return await ConnectionHttp.SendThrowingAsync(client, request, ct);
    }

    public async Task<string> UpdateAsync(ConnectionToken token, string payloadJson, CancellationToken ct)
    {
        var (objectId, fields) = ParseUpdatePayload(payloadJson);
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{ResolveInstanceUrl(token)}/services/data/{options.ApiVersion}/sobjects/{objectId}")
        {
            Content = new StringContent(fields.GetRawText(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        await ConnectionHttp.SendThrowingAsync(client, request, ct);
        return objectId;
    }

    private string ResolveInstanceUrl(ConnectionToken token) =>
        string.IsNullOrWhiteSpace(token.InstanceUrl) ? options.LoginHost : token.InstanceUrl;

    private static (string ObjectId, JsonElement Fields) ParseUpdatePayload(string payloadJson)
    {
        var root = JsonElement.Parse(payloadJson);
        return (root.GetProperty("objectId").GetString() ?? "", root.GetProperty("fields"));
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string> values) =>
        string.Join("&", values.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
}
