using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainSiloHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainSilo(
        this IHostApplicationBuilder builder,
        Action<ISiloBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Clustering());
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Reminders());
        builder.UseOrleans(silo =>
        {
            silo.AddDigitalBrainJournalStorage(builder.Configuration);
            configure?.Invoke(silo);
        });
        builder.AddDigitalBrainOwner(activateOnStart: false);
        return builder;
    }
}
