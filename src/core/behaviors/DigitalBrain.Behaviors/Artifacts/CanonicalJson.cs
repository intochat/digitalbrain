namespace DigitalBrain.Behaviors.Runtime.Artifacts;

using System.Buffers;
using System.Text;
using System.Text.Json;
using DigitalBrain.Behaviors.Artifacts;

internal static class CanonicalJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static ReadOnlyMemory<byte> Serialize<T>(T value)
        => Normalize(JsonSerializer.Serialize(value));

    public static ReadOnlyMemory<byte> Normalize(string value, string parameterName = "json")
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        try
        {
            using var document = JsonDocument.Parse(value);
            RejectDuplicateProperties(document.RootElement);
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                Write(writer, document.RootElement);
            }

            return buffer.WrittenMemory.ToArray();
        }
        catch (JsonException exception)
        {
            throw new BehaviorArtifactException($"{parameterName} must be valid JSON.", exception);
        }
    }

    public static string NormalizeToString(string value, string parameterName)
        => StrictUtf8.GetString(Normalize(value, parameterName).Span);

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new BehaviorArtifactException("Canonical JSON cannot contain duplicate object member names.");
                }

                RejectDuplicateProperties(property.Value);
            }

            return;
        }

        if (value.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item);
            }
        }
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Object)
        {
            writer.WriteStartObject();

            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (value.ValueKind is JsonValueKind.Array)
        {
            writer.WriteStartArray();

            foreach (var item in value.EnumerateArray())
            {
                Write(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        value.WriteTo(writer);
    }
}
