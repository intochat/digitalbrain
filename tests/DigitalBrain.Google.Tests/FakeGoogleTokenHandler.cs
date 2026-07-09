using System.Net;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Google.Tests;

public sealed class FakeGoogleTokenHandler(string accessToken, string refreshToken) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        var payload = new Dictionary<string, object?>
        {
            ["access_token"] = accessToken,
            ["refresh_token"] = refreshToken,
            ["expires_in"] = 3600
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        });
    }

    public static string ExtractQueryValue(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var pair in query.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"Query parameter '{key}' not found in '{url}'.");
    }
}
