using System.IO.Pipelines;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Orleans.Journaling;

namespace DigitalBrain.Mcp.Testing;

public static class McpTestEdge
{
    public static void ConfigureMcpEdge(this DigitalBrainTestBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = ConfigureMcpChatEdge(builder);
    }

    public static McpChatEdgeScript ConfigureMcpChatEdge(this DigitalBrainTestBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var script = new McpChatEdgeScript();
        builder.ConfigureServiceEdge(
            services =>
            {
                services.RemoveAll<IMcpClientSessionFactory>();
                services.AddSingleton<IMcpClientSessionFactory>(new ScriptedMcpSessionFactory(script.Mcp));
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(script.Chat);
            },
            script,
            static edge => edge.Reset());
        return script;
    }

    public static McpEdgeScript Mcp(this TestBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        return brain.ServiceEdgeScript<McpChatEdgeScript>().Mcp;
    }

    public static ScriptedChatClient PlannerChat(this TestBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        return brain.ServiceEdgeScript<McpChatEdgeScript>().Chat;
    }
}

public sealed class McpChatEdgeScript
{
    public McpEdgeScript Mcp { get; } = new();

    public ScriptedChatClient Chat { get; } = new();

    public void Reset()
    {
        Mcp.Reset();
        Chat.Reset();
    }
}

public sealed class McpEdgeScript
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<McpServerTool>> _catalogs =
        new(StringComparer.Ordinal);
    private int _sessionCount;

    public int SessionCount
    {
        get
        {
            lock (_gate)
            {
                return _sessionCount;
            }
        }
    }

    public void Catalog(string serverKey, params McpServerTool[] tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);
        ArgumentNullException.ThrowIfNull(tools);

        lock (_gate)
        {
            _catalogs[serverKey] = tools.ToArray();
        }
    }

    public void Reset()
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
#pragma warning disable CA2000 // Stream transports are owned by the MCP client session.
    public async ValueTask<McpClient> OpenAsync(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        CancellationToken cancellationToken,
        McpAuthorizationAmbientState? ambient = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tokenState);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ambient;

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

        // Client dispose closes the duplex stream; complete server cleanup off the caller's dispose path.
        _ = CompleteServerAsync(mcpServer, run);
        return client;
    }
#pragma warning restore CA2000

    private const string ScriptedServerVersion = "test";

    private static async Task CompleteServerAsync(McpServer server, Task run)
    {
        try
        {
            await run.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }
}
