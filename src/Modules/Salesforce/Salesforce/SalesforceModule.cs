using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Salesforce;

[ModuleConfiguration(typeof(SalesforceConfigurationContract))]
[ModuleHosting("DigitalBrain.Salesforce.Aspire.Hosting.SalesforceModuleHosting, DigitalBrain.Modules.Salesforce.Aspire.Hosting")]
public sealed class SalesforceModule : IModule
{
    public const string OAuthConfigurationRoot = "DigitalBrain:Salesforce:OAuth";
    public const string McpEndpointConfigurationKey = "DigitalBrain:Salesforce:Mcp:Endpoint";
    public const string McpEndpointEnvironmentVariable = "DigitalBrain__Salesforce__Mcp__Endpoint";

    public static readonly Uri DefaultMcpEndpoint = new("https://api.salesforce.com/platform/mcp/v1/platform/sobject-all");

    public static ModuleDefinition Define(SalesforceModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(SalesforceModule), new Dictionary<string, string?>
        {
            [McpEndpointConfigurationKey] = (options.McpEndpoint ?? DefaultMcpEndpoint).AbsoluteUri,
            ["DigitalBrain:Salesforce:Hosting:HostMcp"] = options.HostMcp.ToString(),
            ["DigitalBrain:Salesforce:Mcp:AllowLoopback"] = options.UseLocalMcp.ToString(),
            ["DigitalBrain:Salesforce:Hosting:PublicOrigin"] = options.PublicOrigin?.AbsoluteUri ?? "",
        });
    }

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TokenHandoff>();
        services.AddOptions<SalesforceOAuthOptions>().Bind(silo.Configuration.GetSection(SalesforceOAuthOptions.SectionName));
        services.TryAddSingleton(static services => new SalesforceOAuthConfiguration(services.GetRequiredService<IOptions<SalesforceOAuthOptions>>()));
        services.TryAddSingleton<SalesforceLogins>();
        services.TryAddSingleton<ISalesforceTokenExchange, SalesforceTokenExchange>();
        services.TryAddSingleton<SalesforceTokenRefresh>();
        services.TryAddSingleton<SalesforceWriteAccess>();
        services.AddOptions<SalesforceMcpOptions>()
            .Bind(silo.Configuration.GetSection(SalesforceMcpOptions.SectionName))
            .Validate(static options => { _ = options.ResolveEndpoint(); return true; })
            .ValidateOnStart();
        services.TryAddSingleton<ISalesforceProvider>(static services => new SalesforceMcpProvider(
            services.GetRequiredService<IOptions<SalesforceMcpOptions>>().Value.ResolveEndpoint()));
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<SalesforceLogins>()));
        services.AddSalesforceAuthentication(SalesforceLogins.LoginDefinition);
    }
}