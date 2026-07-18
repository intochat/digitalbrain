using System.Text.Json;
using TripRadar.Bot.Notifications.Format;
using TripRadar.Bot.Notifications.Tracking;
using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Notifications.Handlers;

internal sealed class EventQueryHandler(
    ITrackingRegistry trackingRegistry,
    INotificationDispatcher dispatcher,
    MiniAppLinkBuilder linkBuilder,
    ILogger<EventQueryHandler> logger) : IScheduledQueryHandler
{
    public string Topic => "Events";

    public async Task HandleAsync(string rawJson, CancellationToken ct)
    {
        var parsed = TryParse(rawJson);
        if (parsed is null)
            return;

        var ev = parsed.Value;

        if (!trackingRegistry.TryGetChatId(ev.Username, out var chatId))
        {
            logger.LogDebug("Event for unregistered user @{Username}, ignored", ev.Username);
            return;
        }

        if (ev.TopResult is null || string.IsNullOrWhiteSpace(ev.TopResult.Value.Title))
            return;

        var fingerprint = $"{ev.TopResult.Value.Title}|{ev.TopResult.Value.StartDate}";
        var hasSnapshot = trackingRegistry.TryGetSnapshot(ev.Username, ServiceType.Event, out var snapshot);
        var lastFingerprint = hasSnapshot ? snapshot.Payload : null;

        if (lastFingerprint is null || string.Equals(lastFingerprint, fingerprint, StringComparison.Ordinal))
        {
            UpsertSnapshot(ev.Username, chatId, ev.RequestId, fingerprint);
            if (lastFingerprint is null)
                logger.LogInformation("Baseline event {Fingerprint} set for @{Username}", fingerprint, ev.Username);
            return;
        }

        var envelope = BuildEnvelope(ev);
        await dispatcher.SendAsync(chatId, envelope, ct);

        UpsertSnapshot(ev.Username, chatId, ev.RequestId, fingerprint);
        logger.LogInformation("Sent event alert to @{Username} ({ChatId})", ev.Username, chatId);
    }

    private void UpsertSnapshot(string username, long chatId, Guid requestId, string fingerprint)
    {
        trackingRegistry.UpsertSnapshot(new TrackingSnapshot(
            Username: username,
            ChatId: chatId,
            ServiceType: ServiceType.Event,
            RequestId: requestId,
            Payload: fingerprint,
            UpdatedAtUtc: DateTimeOffset.UtcNow));
    }

    private NotificationEnvelope BuildEnvelope(EventEvent ev)
    {
        var top = ev.TopResult!.Value;

        var summary = ev.Query ?? string.Empty;
        var main = $"Найдено новое событие: {top.Title}";

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(top.VenueName))
            details.Add($"Место: {top.VenueName}");
        if (!string.IsNullOrWhiteSpace(top.When))
            details.Add($"Время: {top.When}");
        if (!string.IsNullOrWhiteSpace(top.Address))
            details.Add($"Адрес: {top.Address}");

        return new NotificationEnvelope(
            TypeLabel: NotificationStrings.TypeLabels.Event,
            RequestSummary: summary,
            MainResult: main,
            Details: details,
            DeepLinkUrl: linkBuilder.ForResult(ServiceType.Event, ev.RequestId));
    }

    private static EventEvent? TryParse(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var username = HandlerJson.GetUsername(root);
        if (string.IsNullOrWhiteSpace(username))
            return null;

        if (!HandlerJson.TryGetEventData(root, out var data))
            return null;

        string? query = null;
        if (data.TryGetProperty("search_parameters", out var search))
            query = HandlerJson.TryGetString(search, "query") ?? HandlerJson.TryGetString(search, "q");

        EventResult? top = null;
        if (data.TryGetProperty("events_results", out var results)
            && results.ValueKind == JsonValueKind.Array
            && results.GetArrayLength() > 0)
        {
            top = ParseEventResult(results[0]);
        }

        return new EventEvent(username!, HandlerJson.GetEventId(root), query, top);
    }

    private static EventResult ParseEventResult(JsonElement element)
    {
        var title = HandlerJson.TryGetString(element, "title");
        string? startDate = null;
        string? when = null;
        if (element.TryGetProperty("date", out var date))
        {
            startDate = HandlerJson.TryGetString(date, "start_date");
            when = HandlerJson.TryGetString(date, "when");
        }

        string? address = null;
        if (element.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.Array && addr.GetArrayLength() > 0)
            address = addr[0].GetString();

        string? venueName = null;
        if (element.TryGetProperty("venue", out var venue))
            venueName = HandlerJson.TryGetString(venue, "name");

        return new EventResult(title, startDate, when, address, venueName);
    }

    private readonly record struct EventEvent(
        string Username,
        Guid RequestId,
        string? Query,
        EventResult? TopResult);

    private readonly record struct EventResult(
        string? Title,
        string? StartDate,
        string? When,
        string? Address,
        string? VenueName);
}
