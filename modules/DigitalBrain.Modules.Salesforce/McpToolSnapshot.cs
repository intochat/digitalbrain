using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Salesforce;

internal sealed record McpToolSnapshot(
    string Name,
    JsonElement InputSchema,
    bool? ReadOnly,
    bool? Destructive,
    string SchemaFingerprint)
{
    internal static McpToolSnapshot Create(
        string name,
        JsonElement inputSchema,
        bool? readOnly,
        bool? destructive)
        => new(
            name,
            inputSchema.Clone(),
            readOnly,
            destructive,
            Fingerprint(inputSchema));

    private static string Fingerprint(JsonElement schema)
    {
        var canonical = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(canonical))
        {
            WriteCanonical(writer, schema);
        }

        return Convert.ToHexString(SHA256.HashData(canonical.WrittenSpan));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            writer.WriteStartObject();

            foreach (var property in element.EnumerateObject().OrderBy(
                property => property.Name,
                StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind is JsonValueKind.Array)
        {
            writer.WriteStartArray();

            foreach (var item in element.EnumerateArray())
            {
                WriteCanonical(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }
}
