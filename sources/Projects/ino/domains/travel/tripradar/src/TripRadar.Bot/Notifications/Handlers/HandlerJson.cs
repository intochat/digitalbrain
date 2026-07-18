using System.Globalization;
using System.Text.Json;

namespace TripRadar.Bot.Notifications.Handlers;

internal static class HandlerJson
{
    public static string? GetUsername(JsonElement root)
    {
        if (!root.TryGetProperty("eventOwner", out var owner))
            return null;
        if (!owner.TryGetProperty("username", out var username))
            return null;
        return username.GetString();
    }

    public static Guid GetEventId(JsonElement root)
    {
        if (root.TryGetProperty("eventId", out var prop)
            && prop.ValueKind == JsonValueKind.String
            && Guid.TryParse(prop.GetString(), out var guid))
            return guid;
        return Guid.Empty;
    }

    public static bool TryGetEventData(JsonElement root, out JsonElement data)
        => root.TryGetProperty("eventData", out data);

    public static string? TryGetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    public static decimal? TryGetDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var value))
            return value;
        if (prop.ValueKind == JsonValueKind.String
            && decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    public static double? TryGetDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var value))
            return value;
        if (prop.ValueKind == JsonValueKind.String
            && double.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }
}
