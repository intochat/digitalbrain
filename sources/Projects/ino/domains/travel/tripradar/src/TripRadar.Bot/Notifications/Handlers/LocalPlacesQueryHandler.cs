using System.Globalization;
using System.Text.Json;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Notifications.Handlers;

internal sealed class LocalPlacesQueryHandler(
    ITrackingRegistry trackingRegistry,
    INotificationDispatcher dispatcher,
    MiniAppLinkBuilder linkBuilder,
    ILogger<LocalPlacesQueryHandler> logger) : IScheduledQueryHandler
{
    public string Topic => "LocalPlaces";

    public async Task HandleAsync(string rawJson, CancellationToken ct)
    {
        var parsed = TryParse(rawJson);
        if (parsed is null)
            return;

        var ev = parsed.Value;

        if (!trackingRegistry.TryGetChatId(ev.Username, out var chatId))
        {
            logger.LogDebug("LocalPlaces event for unregistered user @{Username}, ignored", ev.Username);
            return;
        }

        if (ev.TopResult is null || string.IsNullOrWhiteSpace(ev.TopResult.Value.PlaceId))
            return;

        var topPlaceId = ev.TopResult.Value.PlaceId!;
        var hasSnapshot = trackingRegistry.TryGetSnapshot(ev.Username, ServiceType.LocalPlaces, out var snapshot);
        var lastPlaceId = hasSnapshot ? snapshot.Payload : null;

        if (lastPlaceId is null || string.Equals(lastPlaceId, topPlaceId, StringComparison.Ordinal))
        {
            UpsertSnapshot(ev.Username, chatId, ev.RequestId, topPlaceId);
            if (lastPlaceId is null)
                logger.LogInformation("Baseline place {PlaceId} set for @{Username}", topPlaceId, ev.Username);
            return;
        }

        var envelope = BuildEnvelope(ev);
        await dispatcher.SendAsync(chatId, envelope, ct);

        UpsertSnapshot(ev.Username, chatId, ev.RequestId, topPlaceId);
        logger.LogInformation("Sent local places alert to @{Username} ({ChatId})", ev.Username, chatId);
    }

    private void UpsertSnapshot(string username, long chatId, Guid requestId, string placeId)
    {
        trackingRegistry.UpsertSnapshot(new TrackingSnapshot(
            Username: username,
            ChatId: chatId,
            ServiceType: ServiceType.LocalPlaces,
            RequestId: requestId,
            Payload: placeId,
            UpdatedAtUtc: DateTimeOffset.UtcNow));
    }

    private NotificationEnvelope BuildEnvelope(LocalPlacesEvent ev)
    {
        var place = ev.TopResult!.Value;

        var summaryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ev.Location))
            summaryParts.Add(ev.Location!);
        if (!string.IsNullOrWhiteSpace(ev.Query))
            summaryParts.Add(ev.Query!);
        var summary = string.Join(", ", summaryParts);

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(place.Title))
            details.Add($"Название: {place.Title}");
        if (place.Rating is { } rating && rating > 0)
            details.Add($"Рейтинг: {rating.ToString("0.#", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(place.Address))
            details.Add($"Адрес: {place.Address}");

        return new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.LocalPlaces,
            RequestSummary: summary,
            MainResult: "Найден новый вариант",
            Details: details,
            DeepLinkUrl: linkBuilder.ForResult(ServiceType.LocalPlaces, ev.RequestId));
    }

    private static LocalPlacesEvent? TryParse(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var username = HandlerJson.GetUsername(root);
        if (string.IsNullOrWhiteSpace(username))
            return null;

        if (!HandlerJson.TryGetEventData(root, out var data))
            return null;

        string? query = null;
        string? location = null;
        if (data.TryGetProperty("search_parameters", out var search))
        {
            query = HandlerJson.TryGetString(search, "q") ?? HandlerJson.TryGetString(search, "query");
            location = HandlerJson.TryGetString(search, "location_requested") ?? HandlerJson.TryGetString(search, "location_used");
        }

        LocalPlace? top = null;
        if (data.TryGetProperty("local_results", out var results)
            && results.ValueKind == JsonValueKind.Array
            && results.GetArrayLength() > 0)
        {
            var first = results[0];
            top = new LocalPlace(
                PlaceId: HandlerJson.TryGetString(first, "place_id"),
                Title: HandlerJson.TryGetString(first, "title"),
                Rating: HandlerJson.TryGetDouble(first, "rating"),
                Address: HandlerJson.TryGetString(first, "address"));
        }

        return new LocalPlacesEvent(username!, HandlerJson.GetEventId(root), query, location, top);
    }

    private readonly record struct LocalPlacesEvent(
        string Username,
        Guid RequestId,
        string? Query,
        string? Location,
        LocalPlace? TopResult);

    private readonly record struct LocalPlace(
        string? PlaceId,
        string? Title,
        double? Rating,
        string? Address);
}
