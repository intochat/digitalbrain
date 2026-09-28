using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Apps;

// app.json is the wire form of AppManifest: camelCase keys, string enums, nulls omitted.
public static class AppManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    public static string Serialize(AppManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, Options);
    }

    public static AppManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<AppManifest>(json, Options)
            ?? throw new AppManifestException("app.json is empty.");
    }
}
