using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Integrations.Mcp;

internal sealed record McpToolProperty(string Name, string JsonType)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(JsonType);
    }
}

internal sealed class McpToolContract
{
    private McpToolContract(
        string name,
        McpToolEffect effect,
        IReadOnlyList<McpToolProperty> requiredProperties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(requiredProperties);

        foreach (var property in requiredProperties)
        {
            ArgumentNullException.ThrowIfNull(property);
            property.Validate();
        }

        if (requiredProperties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count()
            != requiredProperties.Count)
        {
            throw new ArgumentException("Required MCP tool properties must have unique names.", nameof(requiredProperties));
        }

        Name = name;
        Effect = effect;
        RequiredProperties = requiredProperties.ToArray();
    }

    internal string Name { get; }

    internal McpToolEffect Effect { get; }

    internal IReadOnlyList<McpToolProperty> RequiredProperties { get; }

    internal static McpToolContract ReadOnly(string name, params McpToolProperty[] requiredProperties)
        => new(name, McpToolEffect.ReadOnly, requiredProperties);

    internal static McpToolContract Mutation(string name, params McpToolProperty[] requiredProperties)
        => new(name, McpToolEffect.Mutation, requiredProperties);

    internal void Admit(McpToolSnapshot tool, string serverDisplayName)
    {
        var effectMatches = Effect switch
        {
            McpToolEffect.ReadOnly => tool.ReadOnly is true && tool.Destructive is not true,
            McpToolEffect.Mutation => tool.ReadOnly is not true,
            _ => false,
        };

        if (!string.Equals(tool.Name, Name, StringComparison.Ordinal)
            || !effectMatches
            || RequiredProperties.Any(property => !HasRequiredProperty(
                tool.InputSchema,
                property.Name,
                property.JsonType)))
        {
            throw new InvalidOperationException(
                $"{serverDisplayName} MCP tool '{tool.Name}' is incompatible with the admitted '{Name}' contract.");
        }
    }

    private static bool HasRequiredProperty(JsonElement schema, string name, string type)
    {
        if (!schema.TryGetProperty("type", out var schemaType)
            || !string.Equals(schemaType.GetString(), "object", StringComparison.Ordinal)
            || !schema.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty(name, out var property)
            || !property.TryGetProperty("type", out var propertyType)
            || !string.Equals(propertyType.GetString(), type, StringComparison.Ordinal)
            || !schema.TryGetProperty("required", out var required)
            || required.ValueKind is not JsonValueKind.Array)
        {
            return false;
        }

        return required.EnumerateArray().Any(candidate =>
            string.Equals(candidate.GetString(), name, StringComparison.Ordinal));
    }
}

internal enum McpToolEffect
{
    ReadOnly,
    Mutation,
}

internal sealed record McpToolHandle(McpToolContract Contract, string SchemaFingerprint);

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
        => new(name, inputSchema.Clone(), readOnly, destructive, Fingerprint(inputSchema));

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
