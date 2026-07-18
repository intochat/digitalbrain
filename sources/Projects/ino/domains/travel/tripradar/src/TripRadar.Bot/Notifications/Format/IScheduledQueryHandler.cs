namespace TripRadar.Bot.Notifications.Format;

internal interface IScheduledQueryHandler
{
    string Topic { get; }

    Task HandleAsync(string rawJson, CancellationToken ct);
}
