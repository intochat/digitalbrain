using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceModule : IModule
{
    public const string OAuthConfigurationRoot = "DigitalBrain:Salesforce:OAuth";
    public const string McpEndpointConfigurationKey = "DigitalBrain:Salesforce:Mcp:Endpoint";
    public const string McpEndpointEnvironmentVariable = "DigitalBrain__Salesforce__Mcp__Endpoint";

    public static readonly Uri DefaultMcpEndpoint = new("https://api.salesforce.com/platform/mcp/v1/platform/sobject-all");

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddSingleton<ICapabilityHandler, SalesforceUpsertHandler>();
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            services.AddSingleton<ISalesforce, FakeSalesforce>();
            return;
        }

        var endpoint = ReadEndpoint(builder.Configuration);
        if (endpoint is null)
        {
            services.AddSingleton<ISalesforce, NotImplementedSalesforce>();
            return;
        }

        var settings = new SalesforceOAuthConfiguration(builder.Configuration);
        services.AddSingleton(settings);
        services.AddSingleton<SalesforceConnections>();
        services.AddSingleton<SalesforceLogins>();
        services.AddSingleton<IUserActionSource>(static s => s.GetRequiredService<SalesforceLogins>());
        services.AddSingleton<IHttpSurface>(static s => new BrowserLoginSurface(s.GetRequiredService<SalesforceLogins>()));
        services.AddSingleton<IAgentToolSource, SalesforceToolSource>();
        services.AddSingleton<ISalesforce>(s => new McpSalesforce(endpoint, s.GetRequiredService<SalesforceConnections>()));
        services.AddHostedService<BrowserLoginWorker<SalesforceLogins>>();
        services.AddSalesforceAuthentication(settings, SalesforceLogins.LoginDefinition);
    }

    private static McpEndpoint? ReadEndpoint(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var value = configuration[McpEndpointConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "api.salesforce.com" || !uri.IsDefaultPort
            || !uri.AbsolutePath.StartsWith("/platform/mcp/", StringComparison.Ordinal)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{McpEndpointConfigurationKey}' must be an HTTPS hosted MCP endpoint on api.salesforce.com.");
        }

        return new McpEndpoint("salesforce", uri);
    }
}
