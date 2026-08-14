using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;

namespace DigitalBrain.Aspire;

public static class DigitalBrainClientHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainClient(
        this IHostApplicationBuilder builder,
        Action<IClientBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Clustering);
        builder.UseOrleansClient(client => configure?.Invoke(client));
        builder.Services.PostConfigure<ClientMessagingOptions>(static options =>
        {
            options.ResponseTimeout = TimeSpan.FromMinutes(10);
            options.ResponseTimeoutWithDebugger = TimeSpan.FromMinutes(10);
        });
        return builder;
    }
}
