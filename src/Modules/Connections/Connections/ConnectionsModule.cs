using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Connections;

[ModuleConfiguration(typeof(ConnectionsConfigurationContract))]
public sealed class ConnectionsModule : IModule
{
    // "id=https://endpoint;id2=https://endpoint" — the MCP allowlist. Empty means no MCP bridge
    // entries, so a hosted MCP server becomes chat-reachable only when it is explicitly listed.
    public const string McpServersKey = "DigitalBrain:Connections:McpServers";

    public static ModuleDefinition Define() => new(typeof(ConnectionsModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.TryAddSingleton<IConnectionProbe, CompositeConnectionProbe>();
        silo.Services.TryAddSingleton<IWebResearchProvider, DeterministicWebResearchProvider>();
        silo.Services.TryAddSingleton<IWebResearch, WebResearchService>();
        RegisterMcpBridge(silo.Services, silo.Configuration[McpServersKey]);
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapConnections();
    }

    // Registers one IAgentToolSource per allowlisted MCP server. An empty allowlist registers
    // nothing, so a hosted MCP server becomes chat-reachable only when it is explicitly listed.
    internal static void RegisterMcpBridge(IServiceCollection services, string? allowlist)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(allowlist))
        {
            return;
        }

        foreach (var entry in allowlist.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0 || separator == entry.Length - 1)
            {
                throw new InvalidOperationException($"MCP allowlist entry '{entry}' is not of the form id=https://endpoint.");
            }

            var serverId = entry[..separator].Trim();
            var endpoint = entry[(separator + 1)..].Trim();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException($"MCP allowlist entry '{serverId}' requires an absolute HTTP(S) endpoint.");
            }

            services.AddMcpAgentTools(serverId, uri);
        }
    }
}