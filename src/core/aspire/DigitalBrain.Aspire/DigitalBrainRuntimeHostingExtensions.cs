using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainRuntimeHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrain(
        this IHostApplicationBuilder builder,
        IReadOnlyCollection<ICompiledModule> modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Clustering());
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Reminders());
        builder.UseOrleans(silo =>
        {
            silo.AddDigitalBrainJournalStorage(builder.Configuration);
            DigitalBrainRuntime.Add(silo, modules);
        });
        builder.AddDigitalBrainOwner(activateOnStart: false);
        return builder;
    }
}
