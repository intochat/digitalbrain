using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;

namespace DigitalBrain.ModuleTests;

internal static class ModuleEdgeExtensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The caller-owned adapters have empty or explicitly non-owning disposal.")]
    internal static void ConfigureModuleEdges(
        this DigitalBrainTestBuilder builder)
    {
        var chat = new ChatEdgeScript();
        builder.ConfigureChatClient<IChatClient, ChatEdgeScript>(
            [typeof(Llama32)],
            new ScriptedChatClient(chat),
            chat,
            static script => script.Reset());

        var mcp = new McpEdgeScript();
        builder.ConfigureSouthboundMcpTransport<IHttpClientFactory, McpEdgeScript>(
            new ScriptedHttpClientFactory(mcp),
            mcp,
            static script => script.Reset());
    }

    internal static ChatEdgeScript Chat(this TestBrain brain)
        => brain.ChatClientScript<ChatEdgeScript>();

    internal static McpEdgeScript Mcp(this TestBrain brain)
        => brain.SouthboundMcpTransportScript<McpEdgeScript>();

    internal static void ConfigureModuleParameters(this TestBrain brain)
    {
        brain.SetOAuthParameter(
            "DigitalBrain:Security:StateProtectionKey",
            Convert.ToBase64String(Enumerable.Range(0, 32)
                .Select(value => (byte)value)
                .ToArray()));
        brain.SetOAuthParameter(
            "DigitalBrain:Google:Gmail:ClientId",
            "module-tests-google");
        brain.SetOAuthParameter(
            "DigitalBrain:Google:Gmail:ClientSecret",
            "module-tests-secret");
        brain.SetOAuthParameter(
            "DigitalBrain:Google:Gmail:RedirectUri",
            "http://localhost/module-tests-google");
        brain.SetOAuthParameter(
            "DigitalBrain:Salesforce:ClientId",
            "module-tests-salesforce");
        brain.SetOAuthParameter(
            "DigitalBrain:Salesforce:RedirectUri",
            "http://localhost/module-tests-salesforce");
    }
}

internal sealed record ChatCall(
    IReadOnlyList<ChatMessage> Messages,
    ChatOptions? Options);

internal sealed class ChatEdgeScript
{
    private readonly Lock _gate = new();
    private readonly List<ChatCall> _calls = [];
    private readonly Queue<ChatStep> _steps = [];
    private readonly Channel<int> _invocations =
        Channel.CreateUnbounded<int>();
    private readonly Channel<int> _completions =
        Channel.CreateUnbounded<int>();

    internal IReadOnlyList<ChatCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    internal void Reply(string text)
        => Enqueue(new(ChatStepKind.Reply, text));

    internal void Fail(string message)
        => Enqueue(new(ChatStepKind.Fail, message));

    internal ChatBlock Block()
    {
        var block = new ChatBlock();
        Enqueue(new(ChatStepKind.Block, string.Empty, block));
        return block;
    }

    internal ValueTask<int> NextInvocation(
        CancellationToken cancellationToken)
        => _invocations.Reader.ReadAsync(cancellationToken);

    internal ValueTask<int> NextCompletion(
        CancellationToken cancellationToken)
        => _completions.Reader.ReadAsync(cancellationToken);

    internal async Task<ChatResponse> Respond(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        ChatStep step;
        int call;

        lock (_gate)
        {
            var snapshot = messages
                .Select(message => new ChatMessage(
                    message.Role,
                    message.Contents
                        .Select(content => content)
                        .ToArray()))
                .ToArray();
            _calls.Add(new(snapshot, options));
            call = _calls.Count;
            step = _steps.Count > 0
                ? _steps.Dequeue()
                : new(ChatStepKind.Reply, $"reply-{call}");
        }

        await _invocations.Writer.WriteAsync(call, cancellationToken);

        try
        {
            return step.Kind switch
            {
                ChatStepKind.Reply => new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, step.Value)),
                ChatStepKind.Fail => throw new InvalidOperationException(step.Value),
                ChatStepKind.Block => await WaitForRelease(
                    step.Block
                        ?? throw new InvalidOperationException(
                            "A blocked chat step has no release gate."),
                    cancellationToken),
                _ => throw new InvalidOperationException(
                    $"Unknown chat step '{step.Kind}'."),
            };
        }
        finally
        {
            _completions.Writer.TryWrite(call);
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            _steps.Clear();
        }

        while (_invocations.Reader.TryRead(out _))
        {
        }

        while (_completions.Reader.TryRead(out _))
        {
        }

    }

    private static async Task<ChatResponse> WaitForRelease(
        ChatBlock block,
        CancellationToken cancellationToken)
    {
        await block.WaitAsync(cancellationToken);
        throw new OperationCanceledException(
            "The scripted provider call was cancelled at its release boundary.");
    }

    private void Enqueue(ChatStep step)
    {
        lock (_gate)
        {
            _steps.Enqueue(step);
        }
    }

    private sealed record ChatStep(
        ChatStepKind Kind,
        string Value,
        ChatBlock? Block = null);

    private enum ChatStepKind
    {
        Reply,
        Fail,
        Block,
    }
}

internal sealed class ChatBlock
{
    private readonly Channel<bool> _release =
        Channel.CreateBounded<bool>(1);

    internal void Release() => _release.Writer.TryWrite(true);

    internal async ValueTask WaitAsync(CancellationToken cancellationToken)
        => _ = await _release.Reader.ReadAsync(cancellationToken);
}

internal sealed class ScriptedChatClient(ChatEdgeScript script) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => script.Respond(messages, options, cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await script.Respond(
            messages,
            options,
            cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

internal sealed record McpToolCall(string Tool, JsonElement Arguments);

internal sealed class McpEdgeScript : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<McpToolCall> _calls = [];

    internal bool FailUpdate { get; set; }

    internal string? ReconciliationDescription { get; set; }

    internal IReadOnlyList<McpToolCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            FailUpdate = false;
            ReconciliationDescription = null;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        using var payload = request.Content is null
            ? null
            : await JsonDocument.ParseAsync(
                await request.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        var root = payload?.RootElement
            ?? throw new InvalidOperationException(
                "The scripted MCP request has no JSON-RPC payload.");
        var method = root.GetProperty("method").GetString();

        if (method == "notifications/initialized")
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        var id = root.GetProperty("id").Clone();
        var result = method switch
        {
            "initialize" => new
            {
                protocolVersion = root
                    .GetProperty("params")
                    .GetProperty("protocolVersion")
                    .GetString(),
                capabilities = new { },
                serverInfo = new { name = "module-tests", version = "1.0" },
            },
            "tools/list" => new
            {
                tools = Tools(),
            },
            "tools/call" => ToolResult(root),
            _ => throw new InvalidOperationException(
                $"Unexpected MCP method '{method}'."),
        };

        return Json(new { jsonrpc = "2.0", id, result });
    }

    private object ToolResult(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var tool = parameters.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("The MCP tool call has no name.");
        var arguments = parameters.GetProperty("arguments").Clone();

        lock (_gate)
        {
            _calls.Add(new(tool, arguments));
        }

        if (tool == "updateSobjectRecord" && FailUpdate)
        {
            throw new HttpRequestException(
                "The scripted Salesforce update lost its response.");
        }

        var structured = tool switch
        {
            "get_message" => JsonSerializer.SerializeToElement(new
            {
                id = arguments.GetProperty("messageId").GetString(),
                subject = "Module testing",
                sender = "ada@example.test",
                plaintextBody = "Typed Gmail mapping",
            }),
            "updateSobjectRecord" => JsonSerializer.SerializeToElement(
                new { success = true }),
            "soqlQuery" when ReconciliationDescription is { } description =>
                JsonSerializer.SerializeToElement(new
                {
                    records = new[]
                    {
                        new
                        {
                            Id = "001000000000042AAA",
                            Description = description,
                        },
                    },
                }),
            "soqlQuery" => JsonSerializer.SerializeToElement(
                new { records = Array.Empty<object>() }),
            _ => throw new InvalidOperationException(
                $"Unexpected MCP tool '{tool}'."),
        };

        return new
        {
            content = Array.Empty<object>(),
            isError = false,
            structuredContent = structured,
        };
    }

    private static object[] Tools() =>
    [
        new
        {
            name = "get_message",
            inputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["messageId"] = new { type = "string" },
                    ["messageFormat"] = new
                    {
                        type = "string",
                        @enum = new[]
                        {
                            "MESSAGE_FORMAT_UNSPECIFIED",
                            "MINIMAL",
                            "FULL_CONTENT",
                            "METADATA_ONLY",
                        },
                    },
                },
                required = new[] { "messageId" },
            },
            outputSchema = new
            {
                type = "object",
                properties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = new { type = "string" },
                    ["subject"] = new { type = "string" },
                    ["sender"] = new { type = "string" },
                    ["plaintextBody"] = new { type = "string" },
                },
            },
            annotations = new
            {
                readOnlyHint = true,
                destructiveHint = false,
                idempotentHint = true,
                openWorldHint = false,
            },
        },
        SalesforceTool(update: true),
        SalesforceTool(update: false),
    ];

    private static object SalesforceTool(bool update)
        => update
            ? new
            {
                name = "updateSobjectRecord",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["sobject-name"] = new { type = "string" },
                        ["id"] = new { type = "string" },
                        ["body"] = new { type = "object" },
                    },
                    required = new[] { "sobject-name", "id", "body" },
                },
                outputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["success"] = new { type = "boolean" },
                    },
                    required = new[] { "success" },
                },
                annotations = new
                {
                    readOnlyHint = false,
                    destructiveHint = true,
                    idempotentHint = false,
                    openWorldHint = false,
                },
            }
            : new
            {
                name = "soqlQuery",
                inputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["query"] = new { type = "string" },
                    },
                    required = new[] { "query" },
                },
                outputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["records"] = new
                        {
                            type = "array",
                            items = new { type = "object" },
                        },
                    },
                    required = new[] { "records" },
                },
                annotations = new
                {
                    readOnlyHint = true,
                    destructiveHint = false,
                    idempotentHint = true,
                    openWorldHint = false,
                },
            };

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value),
            Encoding.UTF8,
            "application/json"),
    };
}

internal sealed class ScriptedHttpClientFactory(McpEdgeScript script) :
    IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => new(script, disposeHandler: false);
}
