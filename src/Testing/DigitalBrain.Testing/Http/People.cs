using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DigitalBrain.Testing;

// Registered accounts talking to the product routes with their own cookie sessions. Shared by
// the in-process module tier and the E2E tier; failures throw so no assertion library leaks in.
public static class People
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
        if (registered.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Registering '{principal}' returned {(int)registered.StatusCode}: {await registered.Content.ReadAsStringAsync(ct)}");
        }
        var member = await registered.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        return new(client, member.GetProperty("brainId").GetString()!, member.GetProperty("accountId").GetString()!);
    }

    public static async Task<JsonElement> Send(HttpClient client, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: Json) };
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} returned {(int)response.StatusCode}: {text}");
        }
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
