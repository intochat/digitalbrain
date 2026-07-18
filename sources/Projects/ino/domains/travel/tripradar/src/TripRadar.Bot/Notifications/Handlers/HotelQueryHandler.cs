using System.Globalization;
using System.Text.Json;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Notifications.Handlers;

internal sealed class HotelQueryHandler(
    ITrackingRegistry trackingRegistry,
    INotificationDispatcher dispatcher,
    MiniAppLinkBuilder linkBuilder,
    ILogger<HotelQueryHandler> logger) : IScheduledQueryHandler
{
    public string Topic => "Hotels";

    public async Task HandleAsync(string rawJson, CancellationToken ct)
    {
        var parsed = TryParse(rawJson);
        if (parsed is null)
            return;

        var ev = parsed.Value;

        if (!trackingRegistry.TryGetChatId(ev.Username, out var chatId))
        {
            logger.LogDebug("Hotel event for unregistered user @{Username}, ignored", ev.Username);
            return;
        }

        if (ev.CheapestProperty is null)
            return;

        var hasSnapshot = trackingRegistry.TryGetSnapshot(ev.Username, ServiceType.Hotel, out var snapshot);
        var lastPrice = hasSnapshot && decimal.TryParse(snapshot.Payload, NumberStyles.Any, CultureInfo.InvariantCulture, out var lp)
            ? (decimal?)lp
            : null;

        var price = ev.CheapestProperty.Value.RatePerNight;
        if (lastPrice is null || lastPrice == price)
        {
            UpsertSnapshot(ev.Username, chatId, ev.RequestId, price);
            if (lastPrice is null)
                logger.LogInformation("Baseline hotel price {Price} set for @{Username}", price, ev.Username);
            return;
        }

        var envelope = BuildEnvelope(ev);
        await dispatcher.SendAsync(chatId, envelope, ct);

        UpsertSnapshot(ev.Username, chatId, ev.RequestId, price);
        logger.LogInformation("Sent hotel price alert to @{Username} ({ChatId})", ev.Username, chatId);
    }

    private void UpsertSnapshot(string username, long chatId, Guid requestId, decimal price)
    {
        trackingRegistry.UpsertSnapshot(new TrackingSnapshot(
            Username: username,
            ChatId: chatId,
            ServiceType: ServiceType.Hotel,
            RequestId: requestId,
            Payload: price.ToString(CultureInfo.InvariantCulture),
            UpdatedAtUtc: DateTimeOffset.UtcNow));
    }

    private NotificationEnvelope BuildEnvelope(HotelEvent ev)
    {
        var summary = BuildSummary(ev);
        var property = ev.CheapestProperty!.Value;

        var main = $"Найден новый вариант: €{property.RatePerNight.ToString("0.##", CultureInfo.InvariantCulture)} за ночь";

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(property.Name))
            details.Add($"Отель: {property.Name}");
        if (property.Rating is { } rating && rating > 0)
            details.Add($"Рейтинг: {rating.ToString("0.#", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(property.HotelClass))
            details.Add($"Класс: {property.HotelClass}");

        return new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.Hotel,
            RequestSummary: summary,
            MainResult: main,
            Details: details,
            DeepLinkUrl: linkBuilder.ForResult(ServiceType.Hotel, ev.RequestId));
    }

    private static string BuildSummary(HotelEvent ev)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ev.Query))
            parts.Add(ev.Query!);
        if (ev.CheckIn is { } ci && ev.CheckOut is { } co)
            parts.Add($"{ci:dd.MM}–{co:dd.MM}");
        return string.Join(", ", parts);
    }

    private static HotelEvent? TryParse(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var username = HandlerJson.GetUsername(root);
        if (string.IsNullOrWhiteSpace(username))
            return null;

        if (!HandlerJson.TryGetEventData(root, out var data))
            return null;

        string? query = null;
        DateOnly? checkIn = null;
        DateOnly? checkOut = null;
        if (data.TryGetProperty("search_parameters", out var search))
        {
            query = HandlerJson.TryGetString(search, "query") ?? HandlerJson.TryGetString(search, "q");
            var ciRaw = HandlerJson.TryGetString(search, "check_in_date");
            if (DateOnly.TryParse(ciRaw, CultureInfo.InvariantCulture, out var ci)) checkIn = ci;
            var coRaw = HandlerJson.TryGetString(search, "check_out_date");
            if (DateOnly.TryParse(coRaw, CultureInfo.InvariantCulture, out var co)) checkOut = co;
        }

        var cheapest = FindCheapestProperty(data);
        var nights = checkIn is { } a && checkOut is { } b ? b.DayNumber - a.DayNumber : (int?)null;

        return new HotelEvent(username!, HandlerJson.GetEventId(root), query, checkIn, checkOut, nights, cheapest);
    }

    private static HotelProperty? FindCheapestProperty(JsonElement data)
    {
        if (!data.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
            return null;

        HotelProperty? cheapest = null;
        foreach (var prop in properties.EnumerateArray())
        {
            decimal? rate = null;
            if (prop.TryGetProperty("rate_per_night", out var rpn))
                rate = HandlerJson.TryGetDecimal(rpn, "extracted_lowest") ?? HandlerJson.TryGetDecimal(rpn, "value");
            if (rate is null)
                continue;

            var name = HandlerJson.TryGetString(prop, "name");
            var rating = HandlerJson.TryGetDouble(prop, "overall_rating");
            var hotelClass = HandlerJson.TryGetString(prop, "hotel_class");

            var candidate = new HotelProperty(name, rate.Value, rating, hotelClass);
            if (cheapest is null || candidate.RatePerNight < cheapest.Value.RatePerNight)
                cheapest = candidate;
        }
        return cheapest;
    }

    private readonly record struct HotelEvent(
        string Username,
        Guid RequestId,
        string? Query,
        DateOnly? CheckIn,
        DateOnly? CheckOut,
        int? Nights,
        HotelProperty? CheapestProperty);

    private readonly record struct HotelProperty(
        string? Name,
        decimal RatePerNight,
        double? Rating,
        string? HotelClass);
}
