using System.Text.Json;

namespace TripRadar.Server.Comms.Core.Extensions;

public static class JsonExtensions
{
    public static T? GetParameter<T>(this string? jsonString, string key)
    {
        if (string.IsNullOrEmpty(jsonString))
        {
            return default;
        }

        try
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString);
            return parameters != null && parameters.TryGetValue(key, out var value)
                ? value.Deserialize<T>()
                : default;
        }
        catch
        {
            return default;
        }
    }

    public static T? DeserializeAs<T>(this string? jsonString) where T : class
    {
        if (string.IsNullOrEmpty(jsonString))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(jsonString);
        }
        catch
        {
            return null;
        }
    }
}
