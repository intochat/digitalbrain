using System.Text.Json;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Orleans.Journaling;

namespace DigitalBrain.Google;

internal sealed class Gmail : Neuron, IGmail
{
    private const string GetMessageName = "get_message";
    private const string TokensName = "google.gmail.oauth";
    private static readonly string[] FullContentOutputProperties =
        ["id", "subject", "sender", "plaintextBody"];
    private static readonly string[] MessageFormats =
        ["MESSAGE_FORMAT_UNSPECIFIED", "MINIMAL", "FULL_CONTENT", "METADATA_ONLY"];
    private static readonly string[] RequiredInputProperties = ["messageId"];
    private static readonly McpServerDefinition Server = new(
        "google.gmail",
        "DigitalBrain Gmail",
        new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
        "DigitalBrain:Google:Gmail",
        ["https://www.googleapis.com/auth/gmail.readonly"]);
    private readonly McpRuntime _runtime;
    private readonly IDurableValue<byte[]> _tokenState;
    private readonly string _durableIdentity;

    public Gmail(McpRuntime runtime)
    {
        _runtime = runtime;
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _durableIdentity = Id.ToString();
    }

    public async Task<GmailMessage> ReadMessage(
        string messageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        return await _runtime.RunAsync(
            Server,
            _tokenState,
            () => WriteStateAsync(),
            _durableIdentity,
            async (client, callbackCancellation) =>
            {
                var tools = await client.ListToolsAsync(cancellationToken: callbackCancellation);
                var tool = AdmitGetMessage(tools);
                var result = await tool.CallAsync(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["messageId"] = messageId,
                        ["messageFormat"] = "FULL_CONTENT",
                    },
                    cancellationToken: callbackCancellation);
                var content = McpRuntime.RequireStructuredContent(result, Server, GetMessageName);

                return new GmailMessage(
                    Required(content, "id"),
                    Required(content, "subject"),
                    Required(content, "sender"),
                    Required(content, "plaintextBody"));
            },
            cancellationToken);
    }

    internal static McpClientTool AdmitGetMessage(IList<McpClientTool> tools)
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

    private static string Required(JsonElement content, string property)
    {
        if (content.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!;
        }

        throw new InvalidOperationException($"Gmail get_message returned no {property}.");
    }
}
