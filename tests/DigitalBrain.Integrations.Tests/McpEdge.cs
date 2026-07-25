using System.IO.Pipelines;
using System.Text.Json;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Orleans.Journaling;

namespace DigitalBrain.Integrations.Tests;

internal static class McpEdgeExtensions
{
    internal static void ConfigureMcpEdge(this DigitalBrainTestBuilder builder)
    {
        var script = new McpEdgeScript();
        builder.ConfigureMcpSessionFactory(
            new ScriptedMcpSessionFactory(script),
            script,
            static edge => edge.Reset());
    }

    internal static McpEdgeScript Mcp(this TestBrain brain)
        => brain.McpSessionScript<McpEdgeScript>();
}

internal sealed class McpEdgeScript
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<McpServerTool>> _catalogs =
        new(StringComparer.Ordinal);
    private int _sessionCount;

    internal int SessionCount
    {
        get
        {
            lock (_gate)
            {
                return _sessionCount;
            }
        }
    }

    internal void Catalog(string serverKey, params McpServerTool[] tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);
        ArgumentNullException.ThrowIfNull(tools);

        lock (_gate)
        {
            _catalogs[serverKey] = tools.ToArray();
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            _sessionCount = 0;
            _catalogs.Clear();
        }
    }

    internal IReadOnlyList<McpServerTool> ToolsFor(string serverKey)
    {
        lock (_gate)
        {
            _sessionCount++;
            return _catalogs.TryGetValue(serverKey, out var tools)
                ? tools
                : [];
        }
    }
}

internal sealed class ScriptedMcpSessionFactory(McpEdgeScript script) : IMcpClientSessionFactory
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Stream transports are owned by the MCP client/server session and disposed with it.")]
    public async ValueTask<IMcpClientSession> OpenAsync(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        cancellationToken.ThrowIfCancellationRequested();

        var tools = script.ToolsFor(server.Key);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        var clientTransport = new StreamClientTransport(
            clientToServer.Writer.AsStream(),
            serverToClient.Reader.AsStream());

        var mcpServer = McpServer.Create(
            serverTransport,
            new McpServerOptions
            {
                ServerInfo = new Implementation
                {
                    Name = server.DisplayName,
                    Version = "test",
                },
                ToolCollection = [.. tools],
            });
        var run = mcpServer.RunAsync(CancellationToken.None);
        var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken);
        return new ScriptedMcpSession(client, mcpServer, run);
    }

    private sealed class ScriptedMcpSession(
        McpClient client,
        McpServer server,
        Task run) : IMcpClientSession
    {
        public McpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await server.DisposeAsync();
            try
            {
                await run;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}

internal static class AdmittedMcpTools
{
    internal static McpServerTool GmailGetMessage(
        string id,
        string subject,
        string sender,
        string plaintextBody)
        => new FixedSchemaTool(
            GmailGetMessageProtocolTool(
                readOnlyHint: true,
                destructiveHint: false,
                idempotentHint: true,
                openWorldHint: false),
            _ => new CallToolResult
            {
                StructuredContent = JsonSerializer.SerializeToElement(new
                {
                    id,
                    subject,
                    sender,
                    plaintextBody,
                }),
            });

    internal static McpServerTool GmailGetMessageWithIncompatibleAnnotations()
        => new FixedSchemaTool(
            GmailGetMessageProtocolTool(
                readOnlyHint: true,
                destructiveHint: true,
                idempotentHint: true,
                openWorldHint: false),
            _ => throw new InvalidOperationException(
                "Incompatible Gmail get_message must not be invoked."));

    private static Tool GmailGetMessageProtocolTool(
        bool? readOnlyHint,
        bool? destructiveHint,
        bool? idempotentHint,
        bool? openWorldHint)
        => new()
        {
            Name = "get_message",
            InputSchema = Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "messageId": { "type": "string" },
                    "messageFormat": {
                      "type": "string",
                      "enum": [
                        "MESSAGE_FORMAT_UNSPECIFIED",
                        "MINIMAL",
                        "FULL_CONTENT",
                        "METADATA_ONLY"
                      ]
                    }
                  },
                  "required": [ "messageId" ]
                }
                """),
            OutputSchema = Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "id": { "type": "string" },
                    "subject": { "type": "string" },
                    "sender": { "type": "string" },
                    "plaintextBody": { "type": "string" }
                  }
                }
                """),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = readOnlyHint,
                DestructiveHint = destructiveHint,
                IdempotentHint = idempotentHint,
                OpenWorldHint = openWorldHint,
            },
        };

    internal static McpServerTool SalesforceUpdateAccount(bool success = true)
        => new FixedSchemaTool(
            new Tool
            {
                Name = "updateSobjectRecord",
                InputSchema = Parse(
                    """
                    {
                      "type": "object",
                      "properties": {
                        "sobject-name": { "type": "string" },
                        "id": { "type": "string" },
                        "body": { "type": "object" }
                      },
                      "required": [ "sobject-name", "id", "body" ]
                    }
                    """),
                OutputSchema = Parse(
                    """
                    {
                      "type": "object",
                      "properties": {
                        "success": { "type": "boolean" }
                      },
                      "required": [ "success" ]
                    }
                    """),
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = false,
                    DestructiveHint = true,
                    IdempotentHint = false,
                    OpenWorldHint = false,
                },
            },
            _ => new CallToolResult
            {
                StructuredContent = JsonSerializer.SerializeToElement(new { success }),
            });

    internal static McpServerTool SalesforceSoqlQuery(string accountId, string description)
        => new FixedSchemaTool(
            new Tool
            {
                Name = "soqlQuery",
                InputSchema = Parse(
                    """
                    {
                      "type": "object",
                      "properties": {
                        "query": { "type": "string" }
                      },
                      "required": [ "query" ]
                    }
                    """),
                OutputSchema = Parse(
                    """
                    {
                      "type": "object",
                      "properties": {
                        "records": {
                          "type": "array",
                          "items": { "type": "object" }
                        }
                      },
                      "required": [ "records" ]
                    }
                    """),
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = true,
                    DestructiveHint = false,
                    IdempotentHint = true,
                    OpenWorldHint = false,
                },
            },
            _ => new CallToolResult
            {
                StructuredContent = JsonSerializer.SerializeToElement(new
                {
                    records = new[]
                    {
                        new { Id = accountId, Description = description },
                    },
                }),
            });

    private static JsonElement Parse(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

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
