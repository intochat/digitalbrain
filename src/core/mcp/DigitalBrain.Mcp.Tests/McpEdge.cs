using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DigitalBrain.Integrations.Tests;

internal static class AdmittedMcpTools
{
    private const string Type = "type";
    private const string Properties = "properties";
    private const string Required = "required";
    private const string Enum = "enum";
    private const string Items = "items";
    private const string String = "string";
    private const string Object = "object";
    private const string Boolean = "boolean";
    private const string Array = "array";

    private const string MessageId = "messageId";
    private const string MessageFormat = "messageFormat";
    private const string Id = "id";
    private const string SalesforceUpdateAccountName = "updateSobjectRecord";
    private const string SalesforceSoqlQueryName = "soqlQuery";

    private static readonly string[] GmailMessageFormats =
    [
        "MESSAGE_FORMAT_UNSPECIFIED",
        "MINIMAL",
        "FULL_CONTENT",
        "METADATA_ONLY",
    ];

    private static readonly Dictionary<string, object?> StringProp = new() { [Type] = String };
    private static readonly Dictionary<string, object?> ObjectProp = new() { [Type] = Object };
    private static readonly Dictionary<string, object?> BooleanProp = new() { [Type] = Boolean };
    private static readonly Dictionary<string, object?> ArrayOfObjects = new()
    {
        [Type] = Array,
        [Items] = ObjectProp,
    };
    private static readonly Dictionary<string, object?> MessageFormatProp = new()
    {
        [Type] = String,
        [Enum] = GmailMessageFormats,
    };

    private static readonly ToolAnnotations ReadOnlyAdmitted = new()
    {
        ReadOnlyHint = true,
        DestructiveHint = false,
        IdempotentHint = true,
        OpenWorldHint = false,
    };

    private static readonly ToolAnnotations DestructiveAdmitted = new()
    {
        ReadOnlyHint = false,
        DestructiveHint = true,
        IdempotentHint = false,
        OpenWorldHint = false,
    };

    internal static McpServerTool GmailGetMessage(string id, string subject, string sender, string plaintextBody)
        => Fixed(GmailGetMessageProtocolTool(ReadOnlyAdmitted), _ => Structured(new { id, subject, sender, plaintextBody }));

    internal static McpServerTool GmailGetMessageWithPayload(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Fixed(GmailGetMessageProtocolTool(ReadOnlyAdmitted), _ => Structured(payload));
    }

    internal static McpServerTool GmailGetMessageWithToolError()
        => Fixed(GmailGetMessageProtocolTool(ReadOnlyAdmitted), _ => new CallToolResult { IsError = true });

    internal static McpServerTool GmailGetMessageWithIncompatibleAnnotations()
        => Fixed(
            GmailGetMessageProtocolTool(new ToolAnnotations
            {
                ReadOnlyHint = true,
                DestructiveHint = true,
                IdempotentHint = true,
                OpenWorldHint = false,
            }),
            static _ => throw new UnreachableException());

    internal static McpServerTool SalesforceUpdateAccount(bool success = true)
        => Fixed(
            new Tool
            {
                Name = SalesforceUpdateAccountName,
                InputSchema = ObjectSchema(
                    ("sobject-name", StringProp),
                    (Id, StringProp),
                    ("body", ObjectProp)),
                OutputSchema = ObjectSchema(("success", BooleanProp)),
                Annotations = DestructiveAdmitted,
            },
            _ => Structured(new { success }));

    internal static McpServerTool SalesforceSoqlQuery(string accountId, string description)
        => Fixed(
            new Tool
            {
                Name = SalesforceSoqlQueryName,
                InputSchema = ObjectSchema(("query", StringProp)),
                OutputSchema = ObjectSchema(("records", ArrayOfObjects)),
                Annotations = ReadOnlyAdmitted,
            },
            _ => Structured(new
            {
                records = new[]
                {
                    new { Id = accountId, Description = description },
                },
            }));

    private static Tool GmailGetMessageProtocolTool(ToolAnnotations annotations)
        => new()
        {
            Name = IntegrationsFixture.GmailGetMessageTool,
            InputSchema = ObjectSchema(
                required: [MessageId],
                (MessageId, StringProp),
                (MessageFormat, MessageFormatProp)),
            OutputSchema = ObjectSchema(
                required: null,
                (Id, StringProp),
                ("subject", StringProp),
                ("sender", StringProp),
                ("plaintextBody", StringProp)),
            Annotations = annotations,
        };

    private static JsonElement ObjectSchema(params (string Name, object Definition)[] properties)
        => ObjectSchema(required: properties.Select(property => property.Name).ToArray(), properties);

    private static JsonElement ObjectSchema(string[]? required, params (string Name, object Definition)[] properties)
    {
        var shape = new Dictionary<string, object?>
        {
            [Type] = Object,
            [Properties] = properties.ToDictionary(property => property.Name, property => property.Definition, StringComparer.Ordinal),
        };
        if (required is not null)
        {
            shape[Required] = required;
        }

        return JsonSerializer.SerializeToElement(shape);
    }

    private static CallToolResult Structured(object payload)
        => new()
        {
            StructuredContent = JsonSerializer.SerializeToElement(payload),
        };

    private static FixedSchemaTool Fixed(Tool protocolTool, Func<RequestContext<CallToolRequestParams>, CallToolResult> invoke)
        => new(protocolTool, invoke);

    private sealed class FixedSchemaTool(
        Tool protocolTool,
        Func<RequestContext<CallToolRequestParams>, CallToolResult> invoke) : McpServerTool
    {
        public override Tool ProtocolTool { get; } = protocolTool;

        public override IReadOnlyList<object> Metadata { get; } = [];

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(invoke(request));
        }
    }
}
