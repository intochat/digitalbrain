using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Capabilities.Connections;

internal readonly record struct SharedRecipient(string Value)
{
    public static SharedRecipient ForApp(string brainId, string app)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(app);
        return new SharedRecipient($"{brainId}/{app}");
    }

    public static SharedRecipient ForPrincipal(string principalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        return AppPrincipal.AppKeyOf(principalId) is { } appKey && appKey.IndexOf('/', StringComparison.Ordinal) is > 0 and var slash
            ? ForApp(appKey[..slash], appKey)
            : new SharedRecipient("principal:" + principalId);
    }
}

public record ConnectionState
{
    internal const string ValueSecret = "value";
    internal const string AccessSecret = "access";
    internal const string RefreshSecret = "refresh";

    public string AuthMethodId { get; init; } = "";
    public ConnectionStatus Status { get; init; } = ConnectionStatus.Disconnected;

    [JsonConverter(typeof(ConnectionSecretsJsonConverter))]
    public IReadOnlyDictionary<string, SealedSecret> Secrets { get; init; } = new Dictionary<string, SealedSecret>(StringComparer.Ordinal);

    public string? Account { get; init; }
    public string Scopes { get; init; } = "";
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastProbedAt { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<string> SharedInto { get; init; } = [];

    internal bool IsConfigured => Status != ConnectionStatus.Disconnected && Secrets.Count > 0;

    internal bool IsSharedWith(SharedRecipient recipient)
        => SharedInto.Contains(recipient.Value, StringComparer.Ordinal);

    public sealed override string ToString() => $"{GetType().Name} {{ <redacted> }}";
}

internal sealed class ConnectionSecretsJsonConverter : JsonConverter<IReadOnlyDictionary<string, SealedSecret>>
{
    public override IReadOnlyDictionary<string, SealedSecret> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, SealedSecret> value, JsonSerializerOptions options)
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
