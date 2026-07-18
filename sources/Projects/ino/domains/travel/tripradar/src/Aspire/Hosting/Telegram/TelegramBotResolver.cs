using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;

namespace Aspire.Hosting.Telegram;

internal static class TelegramBotResolver
{
    internal static async Task<string> ResolveUsernameAsync(string botToken, CancellationToken ct = default)
    {
        var client = new TelegramBotClient(botToken);
        var me = await client.GetMeAsync(ct);
        return me.Username!;
    }
}
