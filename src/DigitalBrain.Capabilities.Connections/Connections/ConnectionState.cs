using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Capabilities.Connections;

// Mutable on purpose: the base mutates the generic TState in place and writes it.
public class ConnectionState
{
    public string AuthMethodId { get; set; } = "";
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;

    [JsonConverter(typeof(ConnectionSecretsJsonConverter))]
    public Dictionary<string, SealedSecret> Secrets { get; set; } = new(StringComparer.Ordinal);

    public string? Account { get; set; }
    public string Scopes { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastProbedAt { get; set; }
    public string? Detail { get; set; }
    // R146: "{brain}/{app}" of every app of another brain this connection is shared into.
    public string[] SharedInto { get; set; } = [];
}

internal sealed class ConnectionSecretsJsonConverter : JsonConverter<Dictionary<string, SealedSecret>>
{
    public override Dictionary<string, SealedSecret> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, SealedSecret>(StringComparer.Ordinal);
        if (reader.TokenType == JsonTokenType.Null)
        {
            return result;
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var cipher = ReadProperty(prop.Value, "Cipher", "cipher", "Ciphertext", "ciphertext") ?? "";
                    var keyId = ReadProperty(prop.Value, "KeyId", "keyId") ?? "";
                    result[prop.Name] = new SealedSecret { Cipher = cipher, KeyId = keyId };
                }
            }
            return result;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { continue; }
                var name = ReadProperty(item, "Name", "name");
                if (string.IsNullOrEmpty(name)) { continue; }
                var cipher = ReadProperty(item, "Ciphertext", "ciphertext", "Cipher", "cipher") ?? "";
                var keyId = ReadProperty(item, "KeyId", "keyId") ?? "";
                result[name] = new SealedSecret { Cipher = cipher, KeyId = keyId };
            }
            return result;
        }

        throw new JsonException($"Unexpected JSON token {root.ValueKind} for ConnectionState.Secrets.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, SealedSecret> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value is not null)
        {
            foreach (var (name, secret) in value)
            {
                writer.WritePropertyName(name);
                JsonSerializer.Serialize(writer, secret, options);
            }
        }
        writer.WriteEndObject();
    }

    private static string? ReadProperty(JsonElement obj, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }
}
