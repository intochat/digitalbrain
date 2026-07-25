using System.Text.Json;
using ModelContextProtocol.Client;

namespace DigitalBrain.Google;

internal sealed partial class Gmail
{
    private static readonly string[] FullContentOutputProperties =
        ["id", "subject", "sender", "plaintextBody"];
    private static readonly string[] MessageFormats =
        ["MESSAGE_FORMAT_UNSPECIFIED", "MINIMAL", "FULL_CONTENT", "METADATA_ONLY"];
    private static readonly string[] RequiredInputProperties = ["messageId"];

    private static McpClientTool AdmitGetMessage(IList<McpClientTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var matches = tools
            .Where(candidate => string.Equals(candidate.Name, GetMessageName, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            throw Incompatible(GetMessageName);
        }

        var tool = matches[0];
        var annotations = tool.ProtocolTool.Annotations;

        if (!HasInputSchema(tool.ProtocolTool.InputSchema)
            || !HasOutputSchema(tool.ProtocolTool.OutputSchema)
            || annotations?.ReadOnlyHint is not true
            || annotations.DestructiveHint is not false
            || annotations.IdempotentHint is not true
            || annotations.OpenWorldHint is not false)
        {
            throw Incompatible(tool.Name);
        }

        return tool;
    }

    private static bool HasInputSchema(JsonElement schema) =>
        IsObjectSchema(schema, out var properties)
        && HasStringProperty(properties, "messageId")
        && HasStringProperty(properties, "messageFormat")
        && RequiredProperties(schema).SequenceEqual(RequiredInputProperties, StringComparer.Ordinal)
        && properties.GetProperty("messageFormat").TryGetProperty("enum", out var formats)
        && formats.ValueKind is JsonValueKind.Array
        && formats.EnumerateArray()
            .Select(value => value.GetString())
            .SequenceEqual(MessageFormats, StringComparer.Ordinal);

    private static bool HasOutputSchema(JsonElement? schema)
    {
        if (schema is not { } output || !IsObjectSchema(output, out var properties))
        {
            return false;
        }

        return FullContentOutputProperties
            .All(property => HasStringProperty(properties, property));
    }

    private static bool IsObjectSchema(JsonElement schema, out JsonElement properties)
    {
        properties = default;
        return schema.ValueKind is JsonValueKind.Object
            && schema.TryGetProperty("type", out var type)
            && string.Equals(type.GetString(), "object", StringComparison.Ordinal)
            && schema.TryGetProperty("properties", out properties)
            && properties.ValueKind is JsonValueKind.Object;
    }

    private static bool HasStringProperty(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var property)
        && property.ValueKind is JsonValueKind.Object
        && property.TryGetProperty("type", out var type)
        && string.Equals(type.GetString(), "string", StringComparison.Ordinal);

    private static IEnumerable<string?> RequiredProperties(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
        && required.ValueKind is JsonValueKind.Array
            ? required.EnumerateArray().Select(value => value.GetString())
            : [];

    private static InvalidOperationException Incompatible(string tool) =>
        new($"{Server.DisplayName} MCP tool '{tool}' is incompatible with the admitted '{GetMessageName}' contract.");
}
