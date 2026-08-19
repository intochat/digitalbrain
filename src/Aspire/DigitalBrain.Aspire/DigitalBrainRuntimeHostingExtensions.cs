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
        ModuleAssemblies modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Reminders);
        // AppHost's WithGrainStorage(DefaultGrainStorage, grainState) auto-wires the "Default"
        // provider through Orleans' own config-driven discovery, which resolves its
        // BlobServiceClient via GetRequiredKeyedService<BlobServiceClient>("grainstate") — so the
        // keyed client below is the only piece the runtime needs to supply. Setting
        // AzureBlobStorageOptions.BlobServiceClient manually (DurableStateHosting's journal style)
        // does not work here: the auto-wired provider's own Configure delegate runs afterward and
        // unconditionally overwrites it, throwing when no keyed client is registered.
        builder.AddKeyedAzureBlobServiceClient(DigitalBrainNames.GrainState);
        builder.UseOrleans(silo =>
        {
            silo.AddDigitalBrainDurableState(builder.Configuration);
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
