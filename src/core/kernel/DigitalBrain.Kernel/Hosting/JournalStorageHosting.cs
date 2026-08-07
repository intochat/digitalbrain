using DigitalBrain.Abstractions;
using Microsoft.Extensions.Configuration;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

public static class JournalStorageHosting
{
    public static ISiloBuilder AddDigitalBrainJournalStorage(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionName = DigitalBrainResourceNames.JournalConnectionName;
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException(
                $"No '{connectionName}' connection string is configured. A neuron's journals are its durability, so the host refuses to start without durable journal storage.");

        return builder.AddAzureBlobJournalStorage(options => options.ConfigureBlobServiceClient(connectionString));
    }
}
