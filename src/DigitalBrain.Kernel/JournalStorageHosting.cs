using Microsoft.Extensions.Configuration;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

public static class JournalStorageHosting
{
    public const string ConnectionStringName = "journal";

    public static ISiloBuilder AddDigitalBrainJournalStorage(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"No '{ConnectionStringName}' connection string is configured. A neuron's journals are its durability, so the host refuses to start without durable journal storage.");

        return builder.AddAzureBlobJournalStorage(options => options.ConfigureBlobServiceClient(connectionString));
    }
}
