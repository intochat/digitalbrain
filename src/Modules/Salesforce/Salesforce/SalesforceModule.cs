using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        var settings = new SalesforceOAuthConfiguration(builder.Configuration);
        services.TryAddSingleton<TokenHandoff>();
        services.AddSingleton<SalesforceWriteAccess>();
        services.AddSingleton(settings);
        services.AddSingleton<SalesforceLogins>();
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<SalesforceLogins>()));
        var endpoint = ReadEndpoint(builder.Configuration);
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            services.AddSingleton<ISalesforceProvider, FakeSalesforceProvider>();
            services.AddSingleton<ISalesforceTokenExchange, FakeSalesforceTokenExchange>();
        }
        else
        {
            services.AddSingleton<ISalesforceProvider>(new SalesforceMcpProvider(endpoint));
            services.AddSingleton<ISalesforceTokenExchange, SalesforceTokenExchange>();
        }
        services.AddSingleton<SalesforceTokenRefresh>();
        services.AddNativeTool("salesforce_current_account", services => services.GetRequiredService<SalesforceNativeTools>().CreateCurrentAccount());
        services.AddNativeTool("salesforce_schema", services => services.GetRequiredService<SalesforceNativeTools>().CreateSchema());
        services.AddNativeTool("salesforce_user_info", services => services.GetRequiredService<SalesforceNativeTools>().CreateGetUserInfo());
        services.AddNativeTool("salesforce_query", services => services.GetRequiredService<SalesforceNativeTools>().CreateSoqlQuery());
        services.AddNativeTool("prepare_salesforce_record", services => services.GetRequiredService<SalesforceNativeTools>().CreateRecordPreview());
        services.AddNativeTool("prepare_salesforce_record_update", services => services.GetRequiredService<SalesforceNativeTools>().CreateRecordUpdatePreview());
        services.AddSingleton(static services => new SalesforceNativeTools(
            services.GetRequiredService<IGrainFactory>().GetGrain<ISalesforce>(new NeuronId("salesforce", "salesforce").ToGrainId()),
            services.GetRequiredService<TimeProvider>(), services.GetRequiredService<SalesforceLogins>()));
        services.AddSalesforceAuthentication(settings, SalesforceLogins.LoginDefinition);
    }

    private static Uri? ReadEndpoint(Microsoft.Extensions.Configuration.IConfiguration configuration)
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

        return uri;
    }
}
