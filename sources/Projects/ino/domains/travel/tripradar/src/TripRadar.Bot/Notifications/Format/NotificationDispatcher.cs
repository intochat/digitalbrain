using TripRadar.Bot.Telegram;

namespace TripRadar.Bot.Notifications.Format;

internal interface INotificationDispatcher
{
    Task SendAsync(long chatId, NotificationEnvelope envelope, CancellationToken ct);
}

internal sealed class NotificationDispatcher(
    ITelegramBotService bot,
    NotificationEnvelopeRenderer renderer) : INotificationDispatcher
{
    public Task SendAsync(long chatId, NotificationEnvelope envelope, CancellationToken ct)
    {
        var text = renderer.Render(envelope);
        return bot.SendMiniAppLaunchAsync(chatId, text, envelope.DeepLinkUrl, NotificationStrings.Button, ct);
    }
}
