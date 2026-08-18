using Microsoft.Extensions.Configuration;
using Orleans.Journaling;

namespace DigitalBrain.Core;

public static class DurableStateHosting
{
    public const string ConnectionName = "journal"; // must match DigitalBrainNames.JournalConnection — the blob connection backing Orleans.Journaling durable state

    public static ISiloBuilder AddDigitalBrainDurableState(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionName = ConnectionName;
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                "Missing connection string 'journal'. Neuron journals and all durable grain state live in "
                + "Orleans.Journaling blob storage, so the host refuses to start without it.");

        return builder.AddAzureBlobJournalStorage(options => options.ConfigureBlobServiceClient(connectionString));
    }
}
