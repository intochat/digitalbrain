using TripRadar.Bot.Notifications.Format;

namespace TripRadar.Bot.Notifications.Tracking;

public sealed record TrackingSnapshot(
    string Username,
    long ChatId,
    ServiceType ServiceType,
    Guid RequestId,
    string Payload,
    DateTimeOffset UpdatedAtUtc);
