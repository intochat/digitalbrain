using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Integrations.Mcp;

internal static class McpRuntimeHosting
{
    internal const string AuthorizationModeKey = "DigitalBrain:Integrations:Mcp:AuthorizationMode";
    internal const string LocalLoopbackDevelopmentMode = "LocalLoopbackDevelopment";

    internal static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient();
        DurablePayloadProtectionHosting.Configure(services, configuration);
        services.TryAddSingleton<IMcpClientSessionFactory, HttpMcpClientSessionFactory>();
        services.TryAddSingleton<McpRuntime>();
    }
}
