using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.Options;
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
        services.TryAddSingleton<TokenHandoff>();
        services.AddSingleton<SalesforceWriteAccess>();
        services.AddOptions<SalesforceOAuthOptions>().Bind(builder.Configuration.GetSection(SalesforceOAuthOptions.SectionName));
        services.AddSingleton(services => new SalesforceOAuthConfiguration(services.GetRequiredService<IOptions<SalesforceOAuthOptions>>()));
        services.AddSingleton<SalesforceLogins>();
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<SalesforceLogins>()));
        services.AddOptions<SalesforceMcpOptions>()
            .Bind(builder.Configuration.GetSection(SalesforceMcpOptions.SectionName))
            .Validate(options => { _ = options.ResolveEndpoint(); return true; })
            .ValidateOnStart();
        services.AddSingleton<ISalesforceProvider>(services => new SalesforceMcpProvider(
            services.GetRequiredService<IOptions<SalesforceMcpOptions>>().Value.ResolveEndpoint()));
        services.AddSingleton<ISalesforceTokenExchange, SalesforceTokenExchange>();
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
        services.AddSalesforceAuthentication(SalesforceLogins.LoginDefinition);
    }
}
