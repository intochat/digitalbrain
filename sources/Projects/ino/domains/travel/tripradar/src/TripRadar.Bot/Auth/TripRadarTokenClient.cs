using System.Net.Http.Json;
using TripRadar.Bot.Models;

namespace TripRadar.Bot.Auth;

public interface ITripRadarTokenClient
{
    Task<BotResult<(string AccessToken, string RefreshToken)>> CreateTelegramSessionAsync(string initData, CancellationToken ct = default);
    Task<BotResult<(string AccessToken, string RefreshToken)>> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
}

internal sealed class TripRadarTokenClient(HttpClient httpClient, ILogger<TripRadarTokenClient> logger) : ITripRadarTokenClient
{
    private const string ClientTypeHeaderName = "X-Client-Type";
    private const string ClientTypeHeaderValue = "api";

    public async Task<BotResult<(string AccessToken, string RefreshToken)>> CreateTelegramSessionAsync(string initData, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tokens/sessions/telegram");
        request.Headers.Add(ClientTypeHeaderName, ClientTypeHeaderValue);
        request.Content = JsonContent.Create(new { initData });

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return BotResult<(string, string)>.Fail($"Telegram session request failed: {(int)response.StatusCode} {body}");

        return ExtractTokenPair(body, "Telegram session response is invalid.");
    }

    public async Task<BotResult<(string AccessToken, string RefreshToken)>> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tokens/refresh-tokens");
        request.Headers.Add(ClientTypeHeaderName, ClientTypeHeaderValue);
        request.Content = JsonContent.Create(new { refreshToken });

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return BotResult<(string, string)>.Fail($"Refresh token request failed: {(int)response.StatusCode} {body}");

        return ExtractTokenPair(body, "Refresh token response is invalid.");
    }

    private BotResult<(string AccessToken, string RefreshToken)> ExtractTokenPair(string json, string fallbackError)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var access = root.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() : null;
            var refresh = root.TryGetProperty("refreshToken", out var refreshProp) ? refreshProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
                return BotResult<(string, string)>.Fail(fallbackError);

            return BotResult<(string, string)>.Ok((access, refresh));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token response parsing failed.");
            return BotResult<(string, string)>.Fail(fallbackError);
        }
    }
}
