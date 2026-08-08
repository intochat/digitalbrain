using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Hosting;
using Orleans.Dashboard;

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
            silo.AddDashboard(options =>
            {
                options.CounterUpdateIntervalMs = 5000;
                options.HistoryLength = 200;
            });
        });
        builder.AddDigitalBrainOwner(activateOnStart: false);
        return builder;
    }
}
