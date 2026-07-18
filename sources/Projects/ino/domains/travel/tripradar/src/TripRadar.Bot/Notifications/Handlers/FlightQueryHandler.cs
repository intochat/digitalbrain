using System.Globalization;
using System.Text.Json;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Notifications.Handlers;

internal sealed class FlightQueryHandler(
    ITrackingRegistry trackingRegistry,
    INotificationDispatcher dispatcher,
    MiniAppLinkBuilder linkBuilder,
    ILogger<FlightQueryHandler> logger) : IScheduledQueryHandler
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public string Topic => "Flights";

    public async Task HandleAsync(string rawJson, CancellationToken ct)
    {
        var parsed = TryParse(rawJson);
        if (parsed is null)
            return;

        var (username, requestId, departure, arrival, departureDate, price) = parsed.Value;

        if (!trackingRegistry.TryGetChatId(username, out var chatId))
        {
            logger.LogDebug("Flight event for unregistered user @{Username}, ignored", username);
            return;
        }

        if (price is null)
            return;

        var hasSnapshot = trackingRegistry.TryGetSnapshot(username, ServiceType.Flight, out var snapshot);
        var lastPrice = hasSnapshot && decimal.TryParse(snapshot.Payload, NumberStyles.Any, CultureInfo.InvariantCulture, out var lp)
            ? (decimal?)lp
            : null;

        if (lastPrice is null || lastPrice == price)
        {
            UpsertSnapshot(username, chatId, requestId, price.Value);
            if (lastPrice is null)
                logger.LogInformation("Baseline flight price {Price} set for @{Username}", price, username);
            return;
        }

        var envelope = BuildEnvelope(requestId, departure, arrival, departureDate, lastPrice.Value, price.Value);
        await dispatcher.SendAsync(chatId, envelope, ct);

        UpsertSnapshot(username, chatId, requestId, price.Value);
        logger.LogInformation("Sent flight price alert to @{Username} ({ChatId})", username, chatId);
    }

    private void UpsertSnapshot(string username, long chatId, Guid requestId, decimal price)
    {
        trackingRegistry.UpsertSnapshot(new TrackingSnapshot(
            Username: username,
            ChatId: chatId,
            ServiceType: ServiceType.Flight,
            RequestId: requestId,
            Payload: price.ToString(CultureInfo.InvariantCulture),
            UpdatedAtUtc: DateTimeOffset.UtcNow));
    }

    private NotificationEnvelope BuildEnvelope(
        Guid requestId,
        string? departure,
        string? arrival,
        DateOnly? departureDate,
        decimal oldPrice,
        decimal newPrice)
    {
        var route = FormatRoute(departure, arrival);
        var dateText = departureDate is { } d ? d.ToString("d MMMM", Russian) : null;
        var summary = string.Join(", ", new[] { route, dateText }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var main = $"Найдена новая цена: €{newPrice.ToString("0.##", CultureInfo.InvariantCulture)}";

        var delta = newPrice - oldPrice;
        var sign = delta < 0 ? "−" : "+";
        var deltaText = $"Изменение: {sign}€{Math.Abs(delta).ToString("0.##", CultureInfo.InvariantCulture)}";

        return new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.Flight,
            RequestSummary: summary,
            MainResult: main,
            Details: new[] { deltaText },
            DeepLinkUrl: linkBuilder.ForResult(ServiceType.Flight, requestId));
    }

    private static string FormatRoute(string? departure, string? arrival)
    {
        if (string.IsNullOrWhiteSpace(departure) && string.IsNullOrWhiteSpace(arrival))
            return string.Empty;
        return $"{departure ?? "?"} → {arrival ?? "?"}";
    }

    private static FlightEvent? TryParse(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var username = HandlerJson.GetUsername(root);
        if (string.IsNullOrWhiteSpace(username))
            return null;

        if (!HandlerJson.TryGetEventData(root, out var data))
            return null;

        string? departure = null;
        string? arrival = null;
        DateOnly? departureDate = null;
        if (data.TryGetProperty("search_parameters", out var search))
        {
            departure = HandlerJson.TryGetString(search, "departure_id");
            arrival = HandlerJson.TryGetString(search, "arrival_id");
            var dateRaw = HandlerJson.TryGetString(search, "outbound_date");
            if (DateOnly.TryParse(dateRaw, CultureInfo.InvariantCulture, out var parsed))
                departureDate = parsed;
        }

        decimal? price = null;
        if (data.TryGetProperty("best_flights", out var best)
            && best.ValueKind == JsonValueKind.Array
            && best.GetArrayLength() > 0)
        {
            price = HandlerJson.TryGetDecimal(best[0], "price");
        }

        return new FlightEvent(username!, HandlerJson.GetEventId(root), departure, arrival, departureDate, price);
    }

    private readonly record struct FlightEvent(
        string Username,
        Guid RequestId,
        string? Departure,
        string? Arrival,
        DateOnly? DepartureDate,
        decimal? Price);
}
