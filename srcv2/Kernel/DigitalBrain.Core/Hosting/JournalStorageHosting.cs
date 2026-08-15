using Microsoft.Extensions.Configuration;
using Orleans.Journaling;

namespace DigitalBrain.Core;

public static class JournalStorageHosting
{
    public const string ConnectionName = "journal"; // must match DigitalBrainNames.JournalConnection

    public static ISiloBuilder AddDigitalBrainJournalStorage(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionName = ConnectionName;
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                $"No '{connectionName}' connection string is configured. A neuron's journals are its durability, so the host refuses to start without durable journal storage.");

        return builder.AddAzureBlobJournalStorage(options => options.ConfigureBlobServiceClient(connectionString));
    }
}
