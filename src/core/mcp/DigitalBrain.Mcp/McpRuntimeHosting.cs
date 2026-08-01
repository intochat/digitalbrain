using DigitalBrain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Mcp;

internal static class McpRuntimeHosting
{
    internal const string PublicSignInBaseKey = "DigitalBrain:Integrations:Mcp:PublicSignInBase";
    internal const string EndpointConfigurationSuffix = "Endpoint";

    internal static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddTransient<McpProtectedResourceMetadataAlignmentHandler>();
        services.AddHttpClient(
                McpRuntime.HttpClientName,
                static client => client.Timeout = TimeSpan.FromMinutes(5))
            .AddHttpMessageHandler<McpProtectedResourceMetadataAlignmentHandler>();
        DurablePayloadProtectionHosting.Configure(services, configuration);
        services.TryAddSingleton<IMcpClientSessionFactory, HttpMcpClientSessionFactory>();
        services.TryAddSingleton<McpRuntime>();
    }
}
