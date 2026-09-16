using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;

namespace DigitalBrain.Telegram;

public static class TelegramWebhook
{
    private static readonly JsonSerializerOptions UpdateJson = CreateUpdateJson();

    public static bool TryReadMessage(JsonElement update, string timeZone, out TelegramMessage? message)
    {
        message = null;
        if (update.ValueKind != JsonValueKind.Object) { return false; }
        try
        {
            return TryReadMessage(update.Deserialize<Update>(UpdateJson), timeZone, out message);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryReadMessage(Update? update, string timeZone, out TelegramMessage? message)
    {
        message = null;
        if (update is not { UpdateId: >= 0, Message: { } body } ||
            body.Chat is not { Type: "private" } chat ||
            body.From is not { Id: > 0, IsBot: false } sender || sender.Id != chat.Id ||
            string.IsNullOrWhiteSpace(body.Text) || body.Text.Length > 4096 || body.Date <= 0 ||
            !TelegramOptions.IsTimeZoneValid(timeZone)) { return false; }
        var eventId = update.UpdateId.ToString(CultureInfo.InvariantCulture);
        message = new TelegramMessage(TelegramAuthentication.CommandIdFor("message", sender.Id, eventId),
            eventId, sender.Id, body.Text, body.Date, timeZone);
        return true;
    }

    private static JsonSerializerOptions CreateUpdateJson()
    {
        // The SDK uses default values for absent value properties. Require the security-relevant
        // wire fields so a missing update_id or is_bot cannot silently become zero or false.
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            foreach (var property in type.Properties)
            {
                if ((type.Type == typeof(Update) && property.Name == "update_id") ||
                    (type.Type == typeof(User) && property.Name is "id" or "is_bot") ||
                    (type.Type == typeof(global::Telegram.BotAPI.AvailableTypes.Chat) && property.Name is "id" or "type") ||
                    (type.Type == typeof(Message) && property.Name == "date"))
                {
                    property.IsRequired = true;
                }
            }
        });
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }
}
