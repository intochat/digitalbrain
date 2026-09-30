using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace DigitalBrain.AI.Agents;

/// <summary>Opens the selected host-registered tools for one execution.</summary>
public interface IAgentToolSource
{
    Task<IAgentToolSession> OpenAsync(IReadOnlyList<string> selectedToolNames,
        Func<AgentToolContext> context, CancellationToken ct);
}

/// <summary>Owns live tool connections until the execution completes.</summary>
public interface IAgentToolSession : IAsyncDisposable
{
    IReadOnlyList<AIFunction> Tools { get; }
}

public static class McpAgentTools
{
    /// <summary>Registers a server. Agents must select exact names of the form mcp_{serverId}_{toolName}.</summary>
    public static IServiceCollection AddMcpAgentTools(this IServiceCollection services, string serverId, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
        { throw new ArgumentException("An absolute HTTP or HTTPS MCP endpoint is required.", nameof(endpoint)); }
        return services.AddMcpAgentTools(serverId, _ => new HttpClientTransport(new() { Endpoint = endpoint }));
    }

    /// <summary>Creates a fresh transport per execution; the execution owns its client and transport.</summary>
    public static IServiceCollection AddMcpAgentTools(this IServiceCollection services, string serverId,
        Func<IServiceProvider, IClientTransport> transportFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        if (serverId.Length > 32 || serverId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        { throw new ArgumentException("MCP server IDs use at most 32 ASCII letters, digits, or hyphens.", nameof(serverId)); }
        services.AddSingleton<IAgentToolSource>(provider => new Source(provider, serverId, transportFactory));
        return services;
    }

    private sealed class Source(IServiceProvider services, string serverId,
        Func<IServiceProvider, IClientTransport> transportFactory) : IAgentToolSource
    {
        public async Task<IAgentToolSession> OpenAsync(IReadOnlyList<string> selectedToolNames,
            Func<AgentToolContext> context, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(selectedToolNames);
            ArgumentNullException.ThrowIfNull(context);
            ct.ThrowIfCancellationRequested();
            var prefix = "mcp_" + serverId + "_";
            var selected = selectedToolNames.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
            if (selected.Length == 0) { return new Session(null, null, []); }
            var transport = transportFactory(services);
            McpClient? client = null;
            try
            {
                client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
                var catalog = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
                var tools = selected.Select(name =>
                {
                    var matches = catalog.Where(tool => prefix + tool.Name == name).ToArray();
                    if (matches.Length != 1)
                    { throw new InvalidOperationException($"Selected MCP tool '{name}' is unavailable or ambiguous."); }
                    return (AIFunction)new CheckedTool(matches[0], name);
                }).ToArray();
                return new Session(client, transport as IAsyncDisposable, tools);
            }
            catch
            {
                try { if (client is not null) { await client.DisposeAsync().ConfigureAwait(false); } }
                finally { if (transport is IAsyncDisposable disposable) { await disposable.DisposeAsync().ConfigureAwait(false); } }
                throw;
            }
        }
    }

    private sealed class Session(McpClient? client, IAsyncDisposable? transport, IReadOnlyList<AIFunction> tools) : IAgentToolSession
    {
        private bool disposed;
        public IReadOnlyList<AIFunction> Tools { get; } = tools;
        public async ValueTask DisposeAsync()
        {
            if (disposed) { return; }
            disposed = true;
            try { if (client is not null) { await client.DisposeAsync().ConfigureAwait(false); } }
            finally { if (transport is not null) { await transport.DisposeAsync().ConfigureAwait(false); } }
        }
    }

    private sealed class CheckedTool(McpClientTool tool, string name) : DelegatingAIFunction(tool)
    {
        public override string Name => name;
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.IsError == true) { throw new InvalidOperationException($"MCP tool '{name}' failed."); }
            return JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
        }
    }
}