using System.Text.Json;

namespace TripRadar.Server.Infrastructure.Services;

internal static class RecentSearchJsonReader
{
    public static string? ReadString(JsonElement root, string parentProperty, string propertyName)
    {
        if (!TryGetNestedProperty(root, parentProperty, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static int? ReadInt(JsonElement root, string parentProperty, string propertyName)
    {
        if (!TryGetNestedProperty(root, parentProperty, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static bool? ReadBool(JsonElement root, string parentProperty, string propertyName)
    {
        if (!TryGetNestedProperty(root, parentProperty, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when property.TryGetInt32(out var numericValue) => numericValue > 0,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var boolValue) => boolValue,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsedNumber) => parsedNumber > 0,
            _ => null
        };
    }

    public static DateTime? ReadDate(JsonElement root, string parentProperty, string propertyName)
    {
        var value = ReadString(root, parentProperty, propertyName);
        if (string.IsNullOrWhiteSpace(value) || !DateTime.TryParse(value, out var parsed))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
    }

    public static string? ReadEnumName<TEnum>(JsonElement root, string parentProperty, string propertyName)
        where TEnum : struct, Enum
    {
        if (!TryGetNestedProperty(root, parentProperty, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (Enum.TryParse<TEnum>(raw, true, out var enumFromString))
            {
                return enumFromString.ToString();
            }

            return raw;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var enumValue) && Enum.IsDefined(typeof(TEnum), enumValue))
        {
            return Enum.GetName(typeof(TEnum), enumValue);
        }

        return null;
    }

    public static IList<string> ReadCsvList(JsonElement root, string parentProperty, string propertyName)
    {
        var value = ReadString(root, parentProperty, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryGetNestedProperty(JsonElement root, string parentProperty, string propertyName, out JsonElement property)
    {
        property = default;
        if (!root.TryGetProperty(parentProperty, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return parent.TryGetProperty(propertyName, out property);
    }
}
