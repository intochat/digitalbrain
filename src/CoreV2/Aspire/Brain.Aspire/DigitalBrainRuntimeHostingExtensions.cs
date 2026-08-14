using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainRuntimeHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainRuntime(
        this IHostApplicationBuilder builder,
        Action<ISiloBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Clustering);
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Reminders);
        builder.AddKeyedAzureBlobServiceClient(DigitalBrainResourceNames.GrainState);
        builder.UseOrleans(silo => configure?.Invoke(silo));
        return builder;
    }
}
