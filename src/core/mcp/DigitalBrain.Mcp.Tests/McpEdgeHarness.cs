using System.IO.Pipelines;
using DigitalBrain.Mcp;
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
        builder.ConfigureMcpSessionFactory(new ScriptedMcpSessionFactory(script), script, static edge => edge.Reset());
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
#pragma warning disable CA2000 // Stream transports are owned by the MCP client/server session.
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
        var serverTransport = new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        var clientTransport = new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());

        var mcpServer = McpServer.Create(
            serverTransport,
            new McpServerOptions
            {
                ServerInfo = new Implementation
                {
                    Name = server.DisplayName,
                    Version = ScriptedServerVersion,
                },
                ToolCollection = [.. tools],
            });
        var run = mcpServer.RunAsync(CancellationToken.None);
        var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken);
        return new ScriptedMcpSession(client, mcpServer, run);
    }
#pragma warning restore CA2000

    private const string ScriptedServerVersion = "test";

    private sealed class ScriptedMcpSession(McpClient client, McpServer server, Task run) : IMcpClientSession
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
