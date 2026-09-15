using System.Globalization;
using System.Text.Json;

namespace DigitalBrain.Telegram;

public static class TelegramWebhook
{
    public static bool TryReadMessage(JsonElement update, string timeZone, out TelegramMessage? message)
    {
        message = null;
        if (update.ValueKind != JsonValueKind.Object ||
            !update.TryGetProperty("update_id", out var updateId) || updateId.ValueKind != JsonValueKind.Number || !updateId.TryGetInt64(out var receipt) || receipt < 0 ||
            !update.TryGetProperty("message", out var body) || body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object ||
            !chat.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "private" ||
            !chat.TryGetProperty("id", out var chatId) || chatId.ValueKind != JsonValueKind.Number || !chatId.TryGetInt64(out var chatIdentity) ||
            !body.TryGetProperty("from", out var sender) || sender.ValueKind != JsonValueKind.Object ||
            !sender.TryGetProperty("id", out var senderId) || senderId.ValueKind != JsonValueKind.Number || !senderId.TryGetInt64(out var userId) || userId <= 0 || userId != chatIdentity ||
            !sender.TryGetProperty("is_bot", out var bot) || bot.ValueKind != JsonValueKind.False ||
            !body.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(text.GetString()) || text.GetString()!.Length > 4096 ||
            !body.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.Number || !date.TryGetInt64(out var sent) || sent is <= 0 or > 253402300799 ||
            !TelegramOptions.IsTimeZoneValid(timeZone)) { return false; }
        var eventId = receipt.ToString(CultureInfo.InvariantCulture);
        message = new TelegramMessage(TelegramAuthentication.CommandIdFor("message", userId, eventId), eventId, userId, text.GetString()!, sent, timeZone);
        return true;
    }
}
