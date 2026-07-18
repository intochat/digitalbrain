using System.Text.Json;
using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;

namespace Ino.Telegram.Host;

/// <summary>
/// Resolves the bot's public HTTPS origin (from <c>Telegram:WebhookUrl</c> or by
/// polling the local ngrok admin API for a tunnel), registers the webhook with
/// Telegram, and publishes the same URL on <see cref="TelegramBotState"/> so
/// the WebApp launch button has a target.
///
/// <para>If neither configuration nor ngrok yields a URL, the service logs a
/// warning and exits cleanly — Telegram won't deliver updates and /start can't
/// surface a mini-app button, but the host stays up so Flutter is still served
/// from this origin. This keeps the demo path resilient when running without
/// a tunnel.</para>
/// </summary>
public sealed class WebhookSetupService(
    ITelegramBotClient botClient,
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramBotOptions> options,
    TelegramBotState botState,
    ILogger<WebhookSetupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.BotToken))
        {
            logger.LogInformation(
                "Telegram bot token not configured — skipping webhook + WebApp setup. " +
                "The host will still serve Flutter from /wwwroot.");
            return;
        }

        var publicUrl = config.WebhookUrl;

        if (string.IsNullOrWhiteSpace(publicUrl) && !string.IsNullOrWhiteSpace(config.NgrokApiUrl))
            publicUrl = await ResolveNgrokUrlAsync(config.NgrokApiUrl, ct);

        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            logger.LogWarning(
                "No webhook URL configured and ngrok not available — Telegram bot will not receive updates");
            return;
        }

        botState.PublicUrl = publicUrl.TrimEnd('/');
        logger.LogInformation("Mini app URL resolved: {Url}", botState.MiniAppUrl);

        var webhookUrl = publicUrl.TrimEnd('/') + "/webhook";

        try
        {
            await botClient.SetWebhookAsync(
                webhookUrl,
                secretToken: string.IsNullOrWhiteSpace(config.WebhookSecretToken) ? null : config.WebhookSecretToken,
                cancellationToken: ct);

            logger.LogInformation("Webhook registered: {Url}", webhookUrl);

            await RegisterBotCommandsAsync(ct);
            await RegisterChatMenuButtonAsync(botState.MiniAppUrl!, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set webhook at {Url}", webhookUrl);
        }
    }

    async Task RegisterBotCommandsAsync(CancellationToken ct)
    {
        try
        {
            // The persistent chat menu button (set in RegisterChatMenuButtonAsync)
            // is the primary launch surface, so the slash menu only needs /start
            // for first-touch onboarding. Text + voice flow through the default
            // handler which forwards to the system silo over gRPC.
            BotCommand[] commands = [new("/start", "Open ino")];
            await botClient.SetMyCommandsAsync(commands, cancellationToken: ct);
            logger.LogInformation("Registered {Count} bot commands", commands.Length);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register bot commands");
        }
    }

    async Task RegisterChatMenuButtonAsync(string miniAppUrl, CancellationToken ct)
    {
        try
        {
            // Sets the always-visible button next to the text input so users can
            // launch the mini-app without sending /start first. chatId omitted →
            // default for all private chats with the bot.
            var menuButton = new MenuButtonWebApp
            {
                Text = "Open ino",
                WebApp = new WebAppInfo(miniAppUrl),
            };
            await botClient.SetChatMenuButtonAsync(menuButton: menuButton, cancellationToken: ct);
            logger.LogInformation("Chat menu button registered → {Url}", miniAppUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register chat menu button");
        }
    }

    async Task<string?> ResolveNgrokUrlAsync(string ngrokApiUrl, CancellationToken ct)
    {
        // Ngrok may take a few seconds to start — retry with linear backoff.
        // The admin API serves /api/tunnels with a list of currently-active
        // tunnels; we want the first HTTPS one (the bot can't use plain HTTP).
        using var http = httpClientFactory.CreateClient();
        var apiBase = ngrokApiUrl.TrimEnd('/');

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2 + attempt), ct);
                var json = await http.GetStringAsync($"{apiBase}/api/tunnels", ct);
                using var doc = JsonDocument.Parse(json);

                foreach (var tunnel in doc.RootElement.GetProperty("tunnels").EnumerateArray())
                {
                    var publicUrl = tunnel.GetProperty("public_url").GetString();
                    if (publicUrl is not null && publicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("Resolved ngrok public URL: {Url}", publicUrl);
                        return publicUrl;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Waiting for ngrok tunnel (attempt {Attempt}): {Message}", attempt + 1, ex.Message);
            }
        }

        logger.LogWarning("Could not resolve ngrok tunnel URL after retries");
        return null;
    }
}
