using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Interactions;
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
    public const string SalesforceConsumerKeyEnvironmentVariable = "DigitalBrain__Integrations__Salesforce__OAuth__ConsumerKey";
    public const string SalesforceConsumerSecretEnvironmentVariable = "DigitalBrain__Integrations__Salesforce__OAuth__ConsumerSecret";
    public const string GmailMcpEndpointEnvironmentVariable = "DigitalBrain__Integrations__Gmail__Mcp__Endpoint";
    public const string SalesforceMcpEndpointEnvironmentVariable = "DigitalBrain__Integrations__Salesforce__Mcp__Endpoint";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ICapabilityHandler, GmailSearchHandler>();
        builder.Services.AddSingleton<ICapabilityHandler, SalesforceUpsertHandler>();
        builder.Services.AddSingleton<ICapabilityHandler, WebSearchHandler>();

        var gmailEndpoint = UseFakeTransports(builder.Configuration) ? null : ReadEndpoint(
            builder.Configuration, GmailMcpEndpointConfigurationKey, "gmail")
            ?? (UseFakeTransports(builder.Configuration) ? null : new McpIntegrationEndpoint("gmail", new Uri(McpIntegrationEndpoint.GmailUri)));
        var salesforceEndpoint = ReadEndpoint(
            builder.Configuration,
            SalesforceMcpEndpointConfigurationKey,
            "salesforce");
        if (gmailEndpoint is not null || salesforceEndpoint is not null)
        {
            builder.Services.TryAddSingleton<IMcpIntegrationClient>(services =>
                new McpIntegrationClient(services.GetService<SalesforceConnections>(), services.GetService<GmailMcpSessions>()));
        }

        if (gmailEndpoint is not null)
        {
            builder.Services.AddSingleton(new GmailOAuthConfiguration(builder.Configuration));
            builder.Services.AddSingleton<GmailConnections>();
            builder.Services.AddSingleton<GmailPendingActions>();
            builder.Services.AddSingleton<GmailMcpSessions>();
            builder.Services.AddSingleton<GmailDraftPreviews>();
            builder.Services.AddSingleton<IUserActionSource>(s => s.GetRequiredService<GmailPendingActions>());
            builder.Services.AddSingleton<ITrustedUserCommandHandler>(s => s.GetRequiredService<GmailDraftPreviews>());
            builder.Services.AddSingleton<IAgentToolSource, GmailToolSource>();
            builder.Services.AddHostedService<GmailCompletionWorker>();
            builder.Services.TryAddSingleton<IGmailTransport>(services =>
                new McpGmailTransport(
                    services.GetRequiredService<IMcpIntegrationClient>(),
                    gmailEndpoint, services.GetRequiredService<GmailPendingActions>()));
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
            builder.Services.AddSingleton(new SalesforceOAuthConfiguration(builder.Configuration));
            builder.Services.AddSingleton<SalesforceConnections>();
            builder.Services.AddHostedService<SalesforceCompletionWorker>();
            builder.Services.AddSingleton<IUserActionSource>(services => services.GetRequiredService<SalesforceConnections>());
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

        return new McpIntegrationEndpoint(name, uri);
    }

    internal static bool UseFakeTransports(Microsoft.Extensions.Configuration.IConfiguration configuration)
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
