using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Orleans.Journaling;

namespace DigitalBrain.Simulations;

internal sealed class FakeMcpHttpServer(JsonElement structuredContent) : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _bearerTokens = new();
    private readonly ConcurrentQueue<McpToolCall> _toolCalls = new();
    private readonly ConcurrentQueue<string> _requestMethods = new();
    internal GmailToolFault GmailFault { get; init; }

    internal bool AdvertiseInvalidSalesforceSchema { get; init; }

    internal bool FailUpdateCalls { get; set; }

    internal string? ReconciliationDescription { get; set; }

    internal bool ToolResultIsError { get; set; }

    internal bool OmitStructuredContent { get; set; }

    internal IReadOnlyList<string> BearerTokens => [.. _bearerTokens];

    internal IReadOnlyList<McpToolCall> ToolCalls => [.. _toolCalls];

    internal IReadOnlyList<string> RequestMethods => [.. _requestMethods];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } token })
        {
            _bearerTokens.Enqueue(token);
        }

        if (request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        var payload = request.Content is null
            ? null
            : await JsonDocument.ParseAsync(
                await request.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        using (payload)
        {
            if (payload?.RootElement.TryGetProperty("method", out var method) is not true)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            var methodName = method.GetString();

            if (methodName == "notifications/initialized")
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            _requestMethods.Enqueue(methodName!);

            var id = payload.RootElement.GetProperty("id").Clone();
            object result = methodName switch
            {
                "initialize" => new
                {
                    protocolVersion = payload.RootElement
                        .GetProperty("params")
                        .GetProperty("protocolVersion")
                        .GetString(),
                    capabilities = new { },
                    serverInfo = new { name = "fake-mcp", version = "1.0" },
                },
                "tools/list" => new
                {
                    tools = Tools(),
                },
                "tools/call" => ToolResult(request, payload.RootElement),
                _ => throw new InvalidOperationException($"Unexpected MCP method '{methodName}'."),
            };

            return Json(new { jsonrpc = "2.0", id, result });
        }
    }

    private object[] Tools()
        =>
        [
            GmailTool(),
            AdvertiseInvalidSalesforceSchema
                ? Tool("update_sobject_record", readOnly: false, "sobject-name", "id")
                : Tool("update_sobject_record", readOnly: false, "sobject-name", "id", "body"),
            Tool("soqlQuery", readOnly: true, "query"),
        ];

    private object GmailTool() => new
    {
        name = GmailFault is GmailToolFault.Name ? "wrong_get_message" : "get_message",
        inputSchema = GmailInputSchema(),
        outputSchema = GmailFault is GmailToolFault.OutputSchemaMissing ? null : GmailOutputSchema(),
        annotations = GmailFault is GmailToolFault.AnnotationsMissing ? null : GmailAnnotations(),
    };

    private object GmailAnnotations() => new
    {
        readOnlyHint = GmailFault is not GmailToolFault.ReadOnly,
        destructiveHint = GmailFault is GmailToolFault.Destructive,
        idempotentHint = GmailFault is not GmailToolFault.Idempotent,
        openWorldHint = GmailFault is GmailToolFault.OpenWorld,
    };

    private object GmailInputSchema() => new
    {
        type = GmailFault is GmailToolFault.InputNonObject ? "array" : "object",
        properties = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["messageId"] = new
            {
                type = GmailFault is GmailToolFault.MessageIdType ? "number" : "string",
            },
            ["messageFormat"] = new
            {
                type = GmailFault is GmailToolFault.MessageFormatType ? "number" : "string",
                @enum = GmailFault is GmailToolFault.MessageFormatEnum
                    ? new[]
                    {
                        "MESSAGE_FORMAT_UNSPECIFIED",
                        "MINIMAL",
                        "FULL_CONTENT",
                        "RAW",
                    }
                    : new[]
                    {
                        "MESSAGE_FORMAT_UNSPECIFIED",
                        "MINIMAL",
                        "FULL_CONTENT",
                        "METADATA_ONLY",
                    },
            },
        },
        required = GmailFault is GmailToolFault.RequiredInputs
            ? new[] { "messageId", "messageFormat" }
            : new[] { "messageId" },
    };

    private object GmailOutputSchema()
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal);
        AddOutputProperty(properties, "id", GmailToolFault.OutputIdMissing, GmailToolFault.OutputIdType);
        AddOutputProperty(
            properties,
            "subject",
            GmailToolFault.OutputSubjectMissing,
            GmailToolFault.OutputSubjectType);
        AddOutputProperty(
            properties,
            "sender",
            GmailToolFault.OutputSenderMissing,
            GmailToolFault.OutputSenderType);
        AddOutputProperty(
            properties,
            "plaintextBody",
            GmailToolFault.OutputPlaintextBodyMissing,
            GmailToolFault.OutputPlaintextBodyType);

        return new
        {
            type = GmailFault is GmailToolFault.OutputNonObject ? "array" : "object",
            properties,
        };
    }

    private void AddOutputProperty(
        Dictionary<string, object> properties,
        string name,
        GmailToolFault missingFault,
        GmailToolFault typeFault)
    {
        var fault = GmailFault;
        if (fault == missingFault)
        {
            return;
        }

        properties[name] = new { type = fault == typeFault ? "number" : "string" };
    }

    private static object Tool(string name, bool readOnly, params string[] properties) => new
    {
        name,
        inputSchema = new
        {
            type = "object",
            properties = properties.ToDictionary(
                property => property,
                property => (object)new { type = property == "body" ? "object" : "string" },
                StringComparer.Ordinal),
            required = properties,
        },
        annotations = new
        {
            readOnlyHint = readOnly,
            destructiveHint = false,
        },
    };

    private Dictionary<string, object?> ToolResult(
        HttpRequestMessage request,
        JsonElement payload)
    {
        var parameters = payload.GetProperty("params");
        var tool = parameters.GetProperty("name").GetString()!;
        _toolCalls.Enqueue(new(
            request.RequestUri!,
            request.Headers.Authorization?.Parameter,
            tool,
            parameters.GetProperty("arguments").Clone()));

        if (tool == "update_sobject_record" && FailUpdateCalls)
        {
            throw new HttpRequestException("Simulated loss after Salesforce invocation began.");
        }

        var content = tool switch
        {
            "update_sobject_record" => JsonSerializer.SerializeToElement(new { success = true }),
            "soqlQuery" => ReconciliationDescription is null
                ? JsonSerializer.SerializeToElement(Array.Empty<object>())
                : JsonSerializer.SerializeToElement(new[]
                {
                    new
                    {
                        Id = "001000000000042AAA",
                        Description = ReconciliationDescription,
                    },
                }),
            _ => structuredContent,
        };

        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["content"] = Array.Empty<object>(),
            ["isError"] = ToolResultIsError,
        };

        if (!OmitStructuredContent)
        {
            result["structuredContent"] = content;
        }

        return result;
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value),
            Encoding.UTF8,
            "application/json"),
    };
}

internal sealed class CancellationProbeHandler : HttpMessageHandler
{
    internal TaskCompletionSource Entered { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The cancellation probe unexpectedly resumed.");
    }
}

internal sealed record McpToolCall(
    Uri Endpoint,
    string? AccessToken,
    string Tool,
    JsonElement Arguments);

internal enum GmailToolFault
{
    None,
    Name,
    InputNonObject,
    MessageIdType,
    MessageFormatType,
    MessageFormatEnum,
    RequiredInputs,
    OutputSchemaMissing,
    OutputNonObject,
    OutputIdMissing,
    OutputIdType,
    OutputSubjectMissing,
    OutputSubjectType,
    OutputSenderMissing,
    OutputSenderType,
    OutputPlaintextBodyMissing,
    OutputPlaintextBodyType,
    AnnotationsMissing,
    ReadOnly,
    Destructive,
    Idempotent,
    OpenWorld,
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class FakeDurableValue<T> : IDurableValue<T>
{
    public T? Value { get; set; }
}
