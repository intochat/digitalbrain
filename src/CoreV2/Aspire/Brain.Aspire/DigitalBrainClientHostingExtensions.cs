using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Hosting;

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
        return builder;
    }
}
