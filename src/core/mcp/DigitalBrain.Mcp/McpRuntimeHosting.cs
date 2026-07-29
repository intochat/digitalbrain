using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Mcp;

internal static class McpRuntimeHosting
{
    internal const string AuthorizationModeKey = "DigitalBrain:Integrations:Mcp:AuthorizationMode";
    internal const string LocalLoopbackDevelopmentMode = "LocalLoopbackDevelopment";
    internal const string EdgeMode = "Edge";
    internal const string PublicSignInBaseKey = "DigitalBrain:Integrations:Mcp:PublicSignInBase";

    internal static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient(
            McpRuntime.HttpClientName,
            static client => client.Timeout = TimeSpan.FromMinutes(5));
        DurablePayloadProtectionHosting.Configure(services, configuration);
        services.TryAddSingleton<IMcpClientSessionFactory, HttpMcpClientSessionFactory>();
        services.TryAddSingleton<McpRuntime>();
    }
}
