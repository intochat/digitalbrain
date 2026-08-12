using DigitalBrain.Modules.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Sdk.Mcp;

public static class McpRuntimeHosting
{
    public const string PublicSignInBaseKey = "DigitalBrain:Integrations:Mcp:PublicSignInBase";
    public const string EndpointConfigurationSuffix = "Endpoint";

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient(
            McpClientSessions.HttpClientName,
            static client => client.Timeout = TimeSpan.FromMinutes(5));
        DurablePayloadProtectionHosting.Configure(services, configuration);
    }
}
