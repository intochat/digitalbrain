using System.Text.Json;

namespace TripRadar.Server.Comms.Core.Extensions;

public static class JsonObjectExtensions
{
    public static string? MergeJsonObjects(
        this string? existingJson,
        string? updatedJson,
        IReadOnlyDictionary<string, object?>? overrides = null)
    {
        var mergedParameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        MergeJsonIntoDictionary(mergedParameters, existingJson);
        MergeJsonIntoDictionary(mergedParameters, updatedJson);

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                mergedParameters[key] = value;
            }
        }

        return mergedParameters.Count == 0 ? null : JsonSerializer.Serialize(mergedParameters);
    }

    private static void MergeJsonIntoDictionary(IDictionary<string, object?> target, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (parameters is null)
            {
                return;
            }

            foreach (var (key, value) in parameters)
            {
                target[key] = value.Clone();
            }
        }
        catch (JsonException)
        {
            // Ignore malformed JSON and preserve existing values.
        }
    }
}
