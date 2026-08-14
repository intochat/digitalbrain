using DigitalBrain.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans.Journaling;

namespace DigitalBrain.Aspire;

public static class DigitalBrainRuntimeHostingExtensions
{
    public static IHostApplicationBuilder AddDigitalBrainRuntime(
        this IHostApplicationBuilder builder,
        Action<ISiloBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var journalConnection = builder.Configuration.GetConnectionString(DigitalBrainResourceNames.Journal)
            ?? throw new InvalidOperationException(
                "No 'journal' connection string is configured. A Neuron's journal is its durability, so the runtime refuses to start without durable journal storage.");

        builder.AddServiceDefaults();
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Clustering);
        builder.AddKeyedAzureTableServiceClient(DigitalBrainResourceNames.Reminders);
        builder.AddKeyedAzureBlobServiceClient(DigitalBrainResourceNames.GrainState);
        builder.AddKeyedAzureBlobServiceClient(DigitalBrainResourceNames.Journal);
        builder.UseOrleans(silo =>
        {
#pragma warning disable ORLEANSEXP005 // Durable journals are the intentional CoreV2 persistence model.
            silo.AddAzureBlobJournalStorage(options => options.ConfigureBlobServiceClient(journalConnection));
#pragma warning restore ORLEANSEXP005
            configure?.Invoke(silo);
        });
        return builder;
    }
}
