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
    private int _catalogReads;

    internal bool AdvertiseInvalidGmailSchema { get; init; }

    internal bool AdvertiseInvalidSalesforceSchema { get; init; }

    internal bool DriftGmailSchemaAfterAdmission { get; init; }

    internal bool ReorderGmailSchemaAfterAdmission { get; init; }

    internal bool ToolResultIsError { get; init; }

    internal bool OmitStructuredContent { get; init; }

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
                "tools/call" => ToolResult(payload.RootElement),
                _ => throw new InvalidOperationException($"Unexpected MCP method '{methodName}'."),
            };

            return Json(new { jsonrpc = "2.0", id, result });
        }
    }

    private object[] Tools()
    {
        var catalogRead = Interlocked.Increment(ref _catalogReads);
        var invalidGmail = AdvertiseInvalidGmailSchema
            || (DriftGmailSchemaAfterAdmission && catalogRead > 1);

        return
        [
            invalidGmail
                ? Tool("get_message", readOnly: true, "messageFormat")
                : ReorderGmailSchemaAfterAdmission && catalogRead > 1
                    ? ReorderedGmailTool()
                    : Tool("get_message", readOnly: true, "messageId", "messageFormat"),
            AdvertiseInvalidSalesforceSchema
                ? Tool("update_sobject_record", readOnly: false, "sobject-name", "id")
                : Tool("update_sobject_record", readOnly: false, "sobject-name", "id", "body"),
            Tool("soqlQuery", readOnly: true, "query"),
        ];
    }

    private static object ReorderedGmailTool() => new
    {
        name = "get_message",
        inputSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["messageFormat"] = new { type = "string" },
                ["messageId"] = new { type = "string" },
            },
            required = new[] { "messageId", "messageFormat" },
        },
        annotations = new
        {
            readOnlyHint = true,
            destructiveHint = false,
        },
    };

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

    private Dictionary<string, object?> ToolResult(JsonElement request)
    {
        var parameters = request.GetProperty("params");
        _toolCalls.Enqueue(new(
            parameters.GetProperty("name").GetString()!,
            parameters.GetProperty("arguments").Clone()));

        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["content"] = Array.Empty<object>(),
            ["isError"] = ToolResultIsError,
        };

        if (!OmitStructuredContent)
        {
            result["structuredContent"] = structuredContent;
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

internal sealed record McpToolCall(string Tool, JsonElement Arguments);

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class FakeDurableValue<T> : IDurableValue<T>
{
    public T? Value { get; set; }
}
