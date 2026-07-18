namespace TripRadar.Bot.Notifications.Format;

public sealed record NotificationEnvelope(
    string TypeLabel,
    string RequestSummary,
    string MainResult,
    IReadOnlyList<string> Details,
    string DeepLinkUrl);
