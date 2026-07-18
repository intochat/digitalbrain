using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;
using TripRadar.Bot.Configuration;

namespace TripRadar.Bot.Telegram;

internal sealed class TelegramWebhookSetup(
    ITelegramBotClient bot,
    IOptions<BotOptions> optionsAccessor,
    ILogger<TelegramWebhookSetup> logger) : BackgroundService
{
    private const int MaxRetryAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsAccessor.Value;
        var webhookUrl = options.WebhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("No webhook URL configured, skipping webhook registration");
            return;
        }

        var fullUrl = webhookUrl.EndsWith("/api/telegram/webhook", StringComparison.OrdinalIgnoreCase)
            ? webhookUrl
            : webhookUrl.TrimEnd('/') + "/api/telegram/webhook";

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                await Task.Delay(RetryDelay, stoppingToken);

                logger.LogInformation("Registering webhook at {Url} (attempt {Attempt}/{Max})",
                    fullUrl, attempt, MaxRetryAttempts);

                await bot.DeleteWebhookAsync(cancellationToken: stoppingToken);

                var secretToken = options.WebhookSecretToken;
                var args = new SetWebhookArgs(fullUrl)
                {
                    SecretToken = string.IsNullOrWhiteSpace(secretToken) ? null : secretToken
                };

                await bot.SetWebhookAsync(args, stoppingToken);

                var info = await bot.GetWebhookInfoAsync(stoppingToken);
                if (string.Equals(info.Url, fullUrl, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Webhook registered successfully: {Url}", info.Url);
                    return;
                }

                logger.LogWarning("Webhook URL mismatch. Expected {Expected}, got {Actual}",
                    fullUrl, info.Url ?? "<null>");
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Webhook setup cancelled");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Webhook setup attempt {Attempt}/{Max} failed", attempt, MaxRetryAttempts);
            }
        }

        logger.LogError("Webhook setup failed after {Max} attempts", MaxRetryAttempts);
    }
}
