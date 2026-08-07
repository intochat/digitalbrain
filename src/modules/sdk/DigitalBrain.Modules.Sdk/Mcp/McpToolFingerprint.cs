using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class McpToolFingerprint
{
    internal static string Create(
        JsonElement inputSchema,
        JsonElement? outputSchema,
        bool? readOnly,
        bool? destructive,
        bool? idempotent,
        bool? openWorld)
    {
        var contract = JsonSerializer.SerializeToElement(new
        {
            inputSchema,
            outputSchema,
            readOnly,
            destructive,
            idempotent,
            openWorld,
        });
        var canonical = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(canonical))
        {
            WriteCanonical(writer, contract);
        }

        return Convert.ToHexString(SHA256.HashData(canonical.WrittenSpan));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            writer.WriteStartObject();

            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
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
