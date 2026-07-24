using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Orleans.Journaling;

namespace DigitalBrain.Simulations;

internal sealed class FakeMcpHttpServer(
    JsonElement structuredContent,
    SalesforceCancellationProbe? salesforceCancellation = null) : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _bearerTokens = new();
    private readonly ConcurrentQueue<McpRequest> _requests = new();
    private readonly ConcurrentQueue<McpToolCall> _toolCalls = new();
    private readonly ConcurrentQueue<string> _requestMethods = new();
    private int _connection;
    internal GmailToolFault GmailFault { get; init; }

    internal SalesforceToolFault SalesforceFault { get; set; }

    internal SalesforceToolFault MutationConnectionFault { get; set; }

    internal SalesforceToolFault ReconciliationConnectionFault { get; set; }

    internal bool FailUpdateCalls { get; set; }

    internal bool BlockReconciliationUntilCancellation { get; set; }

    internal Func<int>? DurableWrites { get; set; }

    internal int? DurableWritesAtUpdate { get; private set; }

    internal string? ReconciliationDescription { get; set; }

    internal bool ToolResultIsError { get; set; }

    internal bool OmitStructuredContent { get; set; }

    internal bool ReconciliationTokenCanBeCanceled { get; private set; }

    internal bool ReconciliationCancellationObserved { get; private set; }

    internal IReadOnlyList<string> BearerTokens => [.. _bearerTokens];

    internal IReadOnlyList<McpRequest> Requests => [.. _requests];

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

            if (methodName == "initialize")
            {
                Interlocked.Increment(ref _connection);
            }

            _requestMethods.Enqueue(methodName!);
            _requests.Enqueue(new(_connection, methodName!));

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
                "tools/call" => await ToolResultAsync(request, payload.RootElement, cancellationToken),
                _ => throw new InvalidOperationException($"Unexpected MCP method '{methodName}'."),
            };

            return Json(new { jsonrpc = "2.0", id, result });
        }
    }

    private object[] Tools()
        =>
        [
            GmailTool(),
            SalesforceTool(update: true),
            SalesforceTool(update: false),
        ];

    private object SalesforceTool(bool update)
    {
        var expectedName = update ? "updateSobjectRecord" : "soqlQuery";
        var nameFault = update ? SalesforceToolFault.UpdateName : SalesforceToolFault.QueryName;
        var inputFault = update ? SalesforceToolFault.UpdateInput : SalesforceToolFault.QueryInput;
        var outputFault = update ? SalesforceToolFault.UpdateOutput : SalesforceToolFault.QueryOutput;
        var annotationsFault = update
            ? SalesforceToolFault.UpdateAnnotations
            : SalesforceToolFault.QueryAnnotations;
        var readOnlyFault = update
            ? SalesforceToolFault.UpdateReadOnly
            : SalesforceToolFault.QueryReadOnly;
        var destructiveFault = update
            ? SalesforceToolFault.UpdateDestructive
            : SalesforceToolFault.QueryDestructive;
        var idempotentFault = update
            ? SalesforceToolFault.UpdateIdempotent
            : SalesforceToolFault.QueryIdempotent;
        var openWorldFault = update
            ? SalesforceToolFault.UpdateOpenWorld
            : SalesforceToolFault.QueryOpenWorld;
        var fault = _connection switch
        {
            2 when MutationConnectionFault is not SalesforceToolFault.None
                => MutationConnectionFault,
            >= 3 when ReconciliationConnectionFault is not SalesforceToolFault.None
                => ReconciliationConnectionFault,
            _ => SalesforceFault,
        };
        var readOnly = !update;
        var destructive = update;
        var idempotent = !update;

        return new
        {
            name = fault == nameFault ? $"wrong-{expectedName}" : expectedName,
            inputSchema = SalesforceInputSchema(update, fault == inputFault),
            outputSchema = SalesforceOutputSchema(update, fault == outputFault),
            annotations = fault == annotationsFault
                ? null
                : new
                {
                    readOnlyHint = fault == readOnlyFault ? !readOnly : readOnly,
                    destructiveHint = fault == destructiveFault ? !destructive : destructive,
                    idempotentHint = fault == idempotentFault ? !idempotent : idempotent,
                    openWorldHint = fault == openWorldFault,
                },
        };
    }

    private static object SalesforceInputSchema(bool update, bool invalid)
    {
        var properties = update
            ? new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sobject-name"] = new { type = "string" },
                ["id"] = new { type = "string" },
                ["body"] = new { type = invalid ? "string" : "object" },
            }
            : new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["query"] = new { type = invalid ? "number" : "string" },
            };

        return new
        {
            type = "object",
            properties,
            required = properties.Keys.ToArray(),
        };
    }

    private static object SalesforceOutputSchema(bool update, bool invalid) =>
        update
            ? new
            {
                type = "object",
                properties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["success"] = new { type = invalid ? "string" : "boolean" },
                },
                required = new[] { "success" },
            }
            : new
            {
                type = "object",
                properties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["records"] = invalid
                        ? new { type = "string" }
                        : (object)new
                        {
                            type = "array",
                            items = new { type = "object" },
                        },
                },
                required = new[] { "records" },
            };

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

    private async Task<Dictionary<string, object?>> ToolResultAsync(
        HttpRequestMessage request,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var parameters = payload.GetProperty("params");
        var tool = parameters.GetProperty("name").GetString()!;
        _toolCalls.Enqueue(new(
            _connection,
            request.RequestUri!,
            request.Headers.Authorization?.Parameter,
            tool,
            parameters.GetProperty("arguments").Clone()));

        if (tool == "updateSobjectRecord" && salesforceCancellation?.Caller is { } caller)
        {
            await caller.CancelAsync();
        }

        if (tool == "updateSobjectRecord")
        {
            DurableWritesAtUpdate = DurableWrites?.Invoke();
        }

        if (tool == "updateSobjectRecord" && FailUpdateCalls)
        {
            throw new HttpRequestException("Simulated loss after Salesforce invocation began.");
        }

        if (tool == "soqlQuery")
        {
            ReconciliationTokenCanBeCanceled = cancellationToken.CanBeCanceled;

            if (BlockReconciliationUntilCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ReconciliationCancellationObserved = true;
                    throw;
                }
            }
        }

        var content = tool switch
        {
            "updateSobjectRecord" => JsonSerializer.SerializeToElement(new { success = true }),
            "soqlQuery" => ReconciliationDescription is null
                ? JsonSerializer.SerializeToElement(new { records = Array.Empty<object>() })
                : JsonSerializer.SerializeToElement(new
                {
                    records = new[]
                    {
                        new
                        {
                            Id = "001000000000042AAA",
                            Description = ReconciliationDescription,
                        },
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
    int Connection,
    Uri Endpoint,
    string? AccessToken,
    string Tool,
    JsonElement Arguments);

internal sealed record McpRequest(int Connection, string Method);

internal sealed class SalesforceCancellationProbe
{
    internal CancellationTokenSource? Caller { get; set; }
}

internal enum SalesforceToolFault
{
    None,
    UpdateName,
    UpdateInput,
    UpdateOutput,
    UpdateAnnotations,
    UpdateReadOnly,
    UpdateDestructive,
    UpdateIdempotent,
    UpdateOpenWorld,
    QueryName,
    QueryInput,
    QueryOutput,
    QueryAnnotations,
    QueryReadOnly,
    QueryDestructive,
    QueryIdempotent,
    QueryOpenWorld,
}

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
