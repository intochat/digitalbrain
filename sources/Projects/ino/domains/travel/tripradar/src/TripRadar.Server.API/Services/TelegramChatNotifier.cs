using System.Net;
using System.Net.Http.Json;

namespace TripRadar.Server.API.Services;

public interface ITelegramChatNotifier
{
    Task<bool> NotifySignedInAsync(long chatId, string username, CancellationToken ct = default);
}

internal sealed class TelegramChatNotifier(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<TelegramChatNotifier> logger) : ITelegramChatNotifier
{
    private const string SignedInPath = "/api/telegram/auth/signed-in";
    private const string SessionSyncSecretHeader = "X-Telegram-Session-Secret";

    public async Task<bool> NotifySignedInAsync(long chatId, string username, CancellationToken ct = default)
    {
        if (chatId <= 0 || string.IsNullOrWhiteSpace(username))
            return false;

        var secret = configuration["Bot:SessionSyncSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogWarning("Bot:SessionSyncSecret is not configured; cannot notify Telegram chat {ChatId}", chatId);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, SignedInPath)
        {
            Content = JsonContent.Create(new { chatId, username })
        };
        request.Headers.TryAddWithoutValidation(SessionSyncSecretHeader, secret);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return true;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            logger.LogWarning("Bot rejected signed-in notification: session sync secret mismatch");
        else
            logger.LogWarning("Bot signed-in notification failed with {StatusCode}", (int)response.StatusCode);

        return false;
    }
}
