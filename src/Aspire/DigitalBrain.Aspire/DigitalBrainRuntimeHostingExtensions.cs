using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Dashboard;

namespace DigitalBrain.Aspire;

public static class DigitalBrainRuntimeHostingExtensions
{
    // Deliberate Azure Queue stream layout for a small single-silo product composition:
    // ~8 physical queues, ~20 streams/queue headroom (2× safety). Visibility is double a
    // one-minute cache window. Azure Queue streams are at-least-once, not rewindable, and
    // not FIFO under failure — weaker than the durable synapse outbox; do not move outbox
    // traffic onto this provider.
    internal const int StreamQueueCount = 8;
    internal static readonly TimeSpan StreamMessageVisibilityTimeout = TimeSpan.FromMinutes(2);

    public static IHostApplicationBuilder AddDigitalBrain(
        this IHostApplicationBuilder builder,
        ModuleAssemblies modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Reminders);
        builder.AddKeyedAzureQueueServiceClient(DigitalBrainNames.Streams);
        builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.PubSub);
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
            silo.Services
                .AddOptions<HashRingStreamQueueMapperOptions>(DigitalBrainNames.StreamProvider)
                .Configure(options => options.TotalQueueCount = StreamQueueCount);
            silo.Services
                .AddOptions<AzureQueueOptions>(DigitalBrainNames.StreamProvider)
                .Configure(options => options.MessageVisibilityTimeout = StreamMessageVisibilityTimeout);
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
