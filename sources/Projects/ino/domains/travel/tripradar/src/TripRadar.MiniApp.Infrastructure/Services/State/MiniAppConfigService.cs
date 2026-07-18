using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class MiniAppConfigService(HttpClient http)
{
    private MiniAppConfig? _cached;

    public async Task<string?> GetWebsiteUrlAsync()
    {
        if (_cached is not null)
            return _cached.WebsiteUrl;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _cached = await http.GetFromJsonAsync<MiniAppConfig>("/api/miniapp/config", cts.Token);
        return _cached?.WebsiteUrl;
    }

    private sealed record MiniAppConfig([property: JsonPropertyName("websiteUrl")] string? WebsiteUrl);
}
