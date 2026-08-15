using DigitalBrain.Core;
using DigitalBrain.Modules.Sdk.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Salesforce;

// Salesforce is a configured MCP server, nothing more: the server's own tool
// catalog is the capability surface, reached through the mcp gateway neuron.
public sealed class SalesforceModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        McpRuntimeHosting.Configure(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(new McpServerDefinition(
            "salesforce",
            "Salesforce",
            new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
            "DigitalBrain:Salesforce",
            ["mcp_api", "refresh_token"],
            requiresClientSecret: false));
        builder.Services.AddSingleton(new ExternalServerCapability("salesforce", "Salesforce"));
    }
}
