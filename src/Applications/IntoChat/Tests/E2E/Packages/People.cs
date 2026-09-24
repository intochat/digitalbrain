using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IntoChat.Tests.E2E.Packages;

// Registered accounts talking to the product routes with their own cookie sessions.
internal static class People
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<SignedInPerson> SignedIn(HttpClient origin, string principal, CancellationToken ct)
    {
        var client = new HttpClient(new SocketsHttpHandler { UseCookies = true, CookieContainer = new CookieContainer() })
        {
            BaseAddress = origin.BaseAddress,
            Timeout = TimeSpan.FromMinutes(5),
        };
        using var registered = await client.PostAsJsonAsync("/identity/register",
            new { principalId = principal, displayName = principal, password = principal + "-password-123" }, Json, ct);
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var member = await registered.Content.ReadFromJsonAsync<DigitalBrain.Identity.Member>(Json, ct);
        return new(client, member!.WorkspaceId);
    }

    public static async Task<JsonElement> Send(HttpClient client, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: Json) };
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} returned {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
