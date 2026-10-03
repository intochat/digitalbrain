using DigitalBrain.Platform.Contracts.Auth;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel;
using DigitalBrain.Sdk;
using DigitalBrain.Platform.Contracts.Integrations.Accounts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Salesforce;

[ModuleId("salesforce")]
public sealed class SalesforceModule : IModule<SalesforceModuleOptions>, IHttpModule
{
    public const string OAuthConfigurationRoot = "DigitalBrain:Salesforce:OAuth";

    public static readonly Uri DefaultMcpEndpoint = new("https://api.salesforce.com/platform/mcp/v1/platform/sobject-all");

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SalesforceOAuthOptions>().Bind(silo.Configuration.GetSection(SalesforceOAuthOptions.SectionName));
        services.TryAddSingleton(static services => new SalesforceOAuthConfiguration(services.GetRequiredService<IOptions<SalesforceOAuthOptions>>()));
        services.TryAddSingleton<SalesforceLogins>();
        services.AddSingleton<BrowserLogins>(s => s.GetRequiredService<SalesforceLogins>());
        services.TryAddSingleton<ISalesforceTokenExchange, SalesforceTokenExchange>();
        services.TryAddSingleton<SalesforceTokenRefresh>();
        services.TryAddSingleton<SalesforceCredentialStore>();
        services.TryAddSingleton<SalesforceWriteAccess>();
        var moduleOptions = silo.Configuration.GetModuleOptions<SalesforceModuleOptions>("salesforce");
        services.AddOptions<SalesforceMcpOptions>()
            .Configure(mcp =>
            {
                mcp.Endpoint = (moduleOptions.McpEndpoint ?? DefaultMcpEndpoint).AbsoluteUri;
                mcp.AllowLoopback = moduleOptions.UseLocalMcp;
            })
            .Validate(static options => { _ = options.ResolveEndpoint(); return true; })
            .ValidateOnStart();
        services.TryAddSingleton<ISalesforceProvider>(static services => new SalesforceMcpProvider(
            services.GetRequiredService<IOptions<SalesforceMcpOptions>>().Value.ResolveEndpoint()));
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<SalesforceLogins>()));
        services.AddSalesforceAuthentication(SalesforceLogins.LoginDefinition);
        services.AddSingleton<IExternalAccount, SalesforceExternalAccount>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        SalesforceAccountEndpoints.Map(endpoints);
    }
}
