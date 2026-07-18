using Confluent.Kafka;
using Microsoft.Extensions.Options;
using TripRadar.Bot.Auth;
using TripRadar.Bot.Configuration;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Tracking;
using BotApiConstants = Telegram.BotAPI.TelegramConstants;
using BotUpdate = Telegram.BotAPI.GettingUpdates.Update;

namespace TripRadar.Bot.Telegram;

internal sealed record SimulatePriceEventsRequest(
    string? Username = null,
    long? ChatId = null,
    string? Departure = null,
    string? Arrival = null,
    string? Date = null,
    decimal[]? Prices = null,
    int? DelayMs = null);

internal sealed record DevRegisterRequest(string Username, long ChatId);

internal sealed record SignedInNotificationRequest(long ChatId, string Username);

public static class TelegramEndpoints
{
    private const string SessionSyncRoute = "/api/telegram/auth/session";
    private const string SignedInRoute = "/api/telegram/auth/signed-in";
    private const string WebhookRoute = "/api/telegram/webhook";
    private const string SessionSyncSecretHeader = "X-Telegram-Session-Secret";
    private const string FlightsPath = "/flights";
    private const int MaxBaselineWaitAttempts = 15;
    private const int BaselineCheckIntervalMs = 1000;

    public static WebApplication MapTelegramEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapPost("/api/dev/register-tracking-user", (
                DevRegisterRequest body,
                ITrackingRegistry trackingRegistry) =>
            {
                trackingRegistry.RegisterUser(body.Username, body.ChatId);
                return Results.Ok(new { registered = true, body.Username, body.ChatId });
            });

            app.MapGet("/api/dev/resolve/{handle}", (
                string handle,
                ITrackingRegistry trackingRegistry) =>
            {
                var normalized = handle.TrimStart('@');
                if (string.IsNullOrWhiteSpace(normalized))
                    return Results.BadRequest(new { error = "Handle is required" });

                if (!trackingRegistry.TryGetChatId(normalized, out var chatId))
                    return Results.NotFound(new
                    {
                        handle = normalized,
                        error = "Unknown handle. The user must /start the bot from their real Telegram account first."
                    });

                return Results.Ok(new { handle = normalized, telegramUserId = chatId, chatId });
            });

            app.MapPost("/api/dev/simulate-price-events", async (
                SimulatePriceEventsRequest? body,
                ITrackingRegistry trackingRegistry,
                IProducer<string, string> producer,
                IOptions<KafkaConsumerOptions> kafkaOptions,
                ILogger<Program> logger,
                CancellationToken ct) =>
            {
                var request = body ?? new SimulatePriceEventsRequest();
                var username = request.Username ?? "tg_100002";
                var chatId = request.ChatId ?? 100002;
                var departure = request.Departure ?? "PRG";
                var arrival = request.Arrival ?? "BCN";
                var date = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd");
                var prices = request.Prices is { Length: > 0 } ? request.Prices : [500m, 380m];
                var delayMs = request.DelayMs ?? 2000;
                _ = kafkaOptions; // reserved for future per-type simulation
                const string topic = "Flights";

                if (request.ChatId.HasValue || !trackingRegistry.TryGetChatId(username, out _))
                {
                    trackingRegistry.RegisterUser(username, chatId);
                    logger.LogInformation("Dev simulation: registered {Username} with chatId {ChatId}", username, chatId);
                }
                else
                {
                    logger.LogInformation("Dev simulation: using existing chatId for {Username}", username);
                }

                for (var i = 0; i < prices.Length; i++)
                {
                    var eventJson = BuildFlightEventJson(username, departure, arrival, date, prices[i]);
                    await producer.ProduceAsync(topic, new Message<string, string>
                    {
                        Key = $"dev-sim-{username}",
                        Value = eventJson
                    }, ct);

                    logger.LogInformation("Dev simulation: published event {Index}/{Total} price={Price} to {Topic}",
                        i + 1, prices.Length, prices[i], topic);

                    if (i == 0 && prices.Length > 1)
                    {
                        var baselineReady = false;
                        for (var attempt = 0; attempt < MaxBaselineWaitAttempts; attempt++)
                        {
                            await Task.Delay(BaselineCheckIntervalMs, ct);
                            if (trackingRegistry.TryGetSnapshot(username, ServiceType.Flight, out _))
                            {
                                baselineReady = true;
                                break;
                            }
                        }

                        if (!baselineReady)
                            logger.LogWarning("Dev simulation: baseline not consumed after {MaxWaitSeconds}s, proceeding anyway",
                                MaxBaselineWaitAttempts * BaselineCheckIntervalMs / 1000);
                    }
                    else if (i < prices.Length - 1)
                    {
                        await Task.Delay(delayMs, ct);
                    }
                }

                return Results.Ok(new { published = prices.Length, username, prices });
            });
        }

        app.MapPost(SignedInRoute, async (
            SignedInNotificationRequest request,
            HttpContext context,
            ITelegramBotService botService,
            IOptions<BotOptions> optionsAccessor,
            ITrackingRegistry trackingRegistry,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var options = optionsAccessor.Value;

            if (!HasValidSessionSyncSecret(context, options))
                return Results.Unauthorized();

            if (request.ChatId <= 0 || string.IsNullOrWhiteSpace(request.Username))
                return Results.BadRequest(new { error = "chatId and username are required" });

            trackingRegistry.RegisterUser(request.Username, request.ChatId);
            logger.LogInformation("Registered chat {ChatId} for @{Username} via signed-in notification", request.ChatId, request.Username);

            await botService.SendMiniAppLaunchAsync(
                request.ChatId,
                NotificationStrings.SignedIn,
                BuildFlightsLaunchUrl(options.MiniAppUrl),
                NotificationStrings.Button,
                ct);

            return Results.Ok(new { delivered = true });
        });

        app.MapPost(SessionSyncRoute, async (
            AuthSessionRequest request,
            HttpContext context,
            AuthSessionSyncHandler syncHandler,
            ITelegramBotService botService,
            IOptions<BotOptions> optionsAccessor,
            ITrackingRegistry trackingRegistry,
            CancellationToken ct) =>
        {
            var options = optionsAccessor.Value;

            if (!HasValidSessionSyncSecret(context, options))
                return Results.Unauthorized();

            var result = await syncHandler.HandleAsync(request, ct);
            if (!result.Success)
                return Results.BadRequest(result.Value);

            if (request.ChatId is > 0 && result.Value?.Username is not null)
            {
                trackingRegistry.RegisterUser(result.Value.Username, request.ChatId.Value);
                _ = botService.SendMiniAppLaunchAsync(
                    request.ChatId.Value,
                    NotificationStrings.SignedIn,
                    BuildFlightsLaunchUrl(options.MiniAppUrl),
                    NotificationStrings.Button);
            }

            return Results.Ok(result.Value);
        });

        app.MapPost(WebhookRoute, async (
            HttpContext context,
            IOptions<BotOptions> optionsAccessor,
            ITelegramBotService botService,
            ITrackingRegistry trackingRegistry,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var options = optionsAccessor.Value;

            if (!HasValidWebhookSecret(context, options, logger))
                return Results.Ok(); // always 200 to Telegram — log and absorb

            BotUpdate? update;
            try
            {
                update = await context.Request.ReadFromJsonAsync<BotUpdate>(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize Telegram update");
                return Results.Ok();
            }

            if (update is null)
                return Results.Ok();

            if (app.Environment.IsDevelopment()
                && update.Message?.Text == "/start"
                && update.Message.Chat?.Id is { } devChatId)
            {
                var telegramUserId = update.Message.From?.Id;
                var telegramHandle = update.Message.From?.Username;
                if (telegramUserId is > 0)
                    trackingRegistry.RegisterUser($"tg_{telegramUserId}", devChatId);
                if (!string.IsNullOrWhiteSpace(telegramHandle))
                    trackingRegistry.RegisterUser(telegramHandle, devChatId);

                // register default simulation username so Aspire commands work without explicit chatId
                trackingRegistry.RegisterUser("tg_100002", devChatId);
                logger.LogInformation(
                    "Dev: registered /start chatId {ChatId} for tg_100002, tg_{TelegramUserId} and @{TelegramHandle}",
                    devChatId, telegramUserId, telegramHandle ?? "(no handle)");
            }

            _ = ProcessUpdateAsync(update, botService, options, trackingRegistry, logger);
            return Results.Ok();
        });

        return app;
    }

    internal static async Task ProcessUpdateAsync(
        BotUpdate update,
        ITelegramBotService botService,
        BotOptions options,
        ITrackingRegistry trackingRegistry,
        ILogger logger)
    {
        try
        {
            if (update.CallbackQuery is { } callbackQuery)
            {
                await botService.AnswerCallbackQueryAsync(callbackQuery.Id);
                return;
            }

            if (update.Message is null)
                return;

            var chatId = update.Message.Chat?.Id;
            if (chatId is null)
                return;

            if (update.Message.Text == "/start")
            {
                var telegramUserId = update.Message.From?.Id;
                await HandleStartAsync(chatId.Value, telegramUserId, botService, trackingRegistry, options, logger, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Telegram update {UpdateId}", update.UpdateId);
        }
    }

    internal static async Task HandleStartAsync(
        long chatId,
        long? telegramUserId,
        ITelegramBotService botService,
        ITrackingRegistry trackingRegistry,
        BotOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        var flightsLaunchUrl = BuildFlightsLaunchUrl(options.MiniAppUrl);

        if (telegramUserId is not > 0)
        {
            await botService.SendMiniAppLaunchAsync(chatId, NotificationStrings.Button, flightsLaunchUrl, NotificationStrings.Button, ct);
            return;
        }
var googleSignInUrl = BuildTelegramGoogleAuthUrl(options.WebsiteUrl, chatId);
        var websiteSignInUrl = BuildWebsiteSignInUrl(options.WebsiteUrl, chatId);
        await botService.SendWelcomeWithRegistrationAsync(
            chatId,
            NotificationStrings.WelcomeUnregistered,
            googleSignInUrl,
            NotificationStrings.ContinueWithGoogle,
            websiteSignInUrl,
            NotificationStrings.RegisterOnWebsite,
            ct);
    }

    internal static string BuildFlightsLaunchUrl(string miniAppUrl) =>
        string.IsNullOrWhiteSpace(miniAppUrl)
            ? string.Empty
            : $"{miniAppUrl.TrimEnd('/')}{FlightsPath}";

    internal static string BuildWebsiteSignInUrl(string websiteUrl, long chatId)
    {
        if (string.IsNullOrWhiteSpace(websiteUrl))
            return string.Empty;

        return $"{websiteUrl.TrimEnd('/')}/signin?source=telegram&chatId={chatId}";
    }

    internal static string BuildTelegramGoogleAuthUrl(string websiteUrl, long chatId)
    {
        if (string.IsNullOrWhiteSpace(websiteUrl))
            return string.Empty;

        return $"{websiteUrl.TrimEnd('/')}/auth/telegram-google?source=telegram&chatId={chatId}";
    }

    internal static bool HasValidSessionSyncSecret(HttpContext context, BotOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SessionSyncSecret))
            return false;

        if (!context.Request.Headers.TryGetValue(SessionSyncSecretHeader, out var headerValue))
            return false;

        return string.Equals(headerValue.ToString(), options.SessionSyncSecret, StringComparison.Ordinal);
    }

    internal static bool HasValidWebhookSecret(HttpContext context, BotOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.WebhookSecretToken))
            return true;

        if (!context.Request.Headers.TryGetValue(BotApiConstants.XTelegramBotApiSecretToken, out var headerValue))
        {
            logger.LogWarning("Rejected webhook request: missing secret token header");
            return false;
        }

        var isValid = string.Equals(headerValue.ToString(), options.WebhookSecretToken, StringComparison.Ordinal);
        if (!isValid)
            logger.LogWarning("Rejected webhook request: invalid secret token");

        return isValid;
    }

    internal static string BuildFlightEventJson(string username, string departure, string arrival, string date, decimal price) =>
        $$"""
        {
          "eventId": "{{Guid.NewGuid()}}",
          "eventDate": "{{DateTimeOffset.UtcNow:O}}",
          "eventOwner": { "username": "{{username}}" },
          "eventData": {
            "search_parameters": {
              "departure_id": "{{departure}}",
              "arrival_id": "{{arrival}}",
              "outbound_date": "{{date}}"
            },
            "best_flights": [{ "price": {{price}} }]
          }
        }
        """;
}
