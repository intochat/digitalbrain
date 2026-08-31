using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Execution;
using DigitalBrain.Integrations.Gmail;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Integrations;

public sealed class IntegrationsModule : Core.IModule
{
    public const string GmailMcpEndpointConfigurationKey = "DigitalBrain:Integrations:Gmail:Mcp:Endpoint";
    public const string SalesforceMcpEndpointConfigurationKey = "DigitalBrain:Integrations:Salesforce:Mcp:Endpoint";
    public const string SalesforceMcpAccessTokenConfigurationKey = "DigitalBrain:Integrations:Salesforce:Mcp:AccessToken";
    public const string GmailMcpEndpointEnvironmentVariable = "DigitalBrain__Integrations__Gmail__Mcp__Endpoint";
    public const string SalesforceMcpEndpointEnvironmentVariable = "DigitalBrain__Integrations__Salesforce__Mcp__Endpoint";
    public const string SalesforceMcpAccessTokenEnvironmentVariable = "DigitalBrain__Integrations__Salesforce__Mcp__AccessToken";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ICapabilityHandler, GmailSearchHandler>();
        builder.Services.AddSingleton<ICapabilityHandler, SalesforceUpsertHandler>();
        builder.Services.AddSingleton<ICapabilityHandler, WebSearchHandler>();

        var gmailEndpoint = ReadEndpoint(
            builder.Configuration,
            GmailMcpEndpointConfigurationKey,
            "gmail");
        var salesforceEndpoint = ReadEndpoint(
            builder.Configuration,
            SalesforceMcpEndpointConfigurationKey,
            "salesforce");
        if (gmailEndpoint is not null || salesforceEndpoint is not null)
        {
            builder.Services.TryAddSingleton<IMcpIntegrationClient, McpIntegrationClient>();
        }

        if (gmailEndpoint is not null)
        {
            builder.Services.TryAddSingleton<IGmailTransport>(services =>
                new McpGmailTransport(
                    services.GetRequiredService<IMcpIntegrationClient>(),
                    gmailEndpoint));
        }
        else if (UseFakeTransports(builder.Configuration))
        {
            builder.Services.TryAddSingleton<IGmailTransport, FakeGmailTransport>();
        }
        else
        {
            builder.Services.TryAddSingleton<IGmailTransport, NotImplementedGmailTransport>();
        }

        if (salesforceEndpoint is not null)
        {
            builder.Services.AddSingleton<IAgentToolSource, SalesforceToolSource>();
            builder.Services.TryAddSingleton<ISalesforceTransport>(services =>
                new McpSalesforceTransport(
                    services.GetRequiredService<IMcpIntegrationClient>(),
                    salesforceEndpoint));
        }
        else if (UseFakeTransports(builder.Configuration))
        {
            builder.Services.TryAddSingleton<ISalesforceTransport, FakeSalesforceTransport>();
        }
        else
        {
            builder.Services.TryAddSingleton<ISalesforceTransport, NotImplementedSalesforceTransport>();
        }

        if (UseFakeTransports(builder.Configuration))
        {
            builder.Services.TryAddSingleton<IWebSearchTransport, FakeWebSearchTransport>();
        }
        else
        {
            builder.Services.TryAddSingleton<IWebSearchTransport, NotImplementedWebSearchTransport>();
        }
    }

    private static McpIntegrationEndpoint? ReadEndpoint(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string key,
        string name)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Configuration '{key}' is not an absolute URI.");
        }

        return new McpIntegrationEndpoint(
            name,
            uri,
            name == "salesforce" ? configuration[SalesforceMcpAccessTokenConfigurationKey] : null);
    }

    private static bool UseFakeTransports(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        if (string.Equals(
                configuration[DigitalBrainNames.Mode],
                DigitalBrainNames.TestingMode,
                StringComparison.Ordinal))
        {
            return true;
        }

        var fakes = configuration[DigitalBrainNames.Fakes];
        return string.Equals(fakes, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fakes, "1", StringComparison.OrdinalIgnoreCase);
    }
}
