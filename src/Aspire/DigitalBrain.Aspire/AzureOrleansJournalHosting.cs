using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Aspire;

internal static class AzureOrleansJournalHosting
{
    internal static ISiloBuilder AddAzureBlobJournal(this ISiloBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(DigitalBrainNames.JournalConnection)
            ?? throw new InvalidOperationException(
                $"Missing connection string '{DigitalBrainNames.JournalConnection}'. "
                + "Neuron journals require Azure Blob storage in this host.");

        builder.AddAzureBlobJournalStorage(options =>
        {
            options.ContainerName = "digitalbrain-v2-journal";
            options.ConfigureBlobServiceClient(connectionString);
        });

        var services = builder.Services;
        var descriptor = services.Last(service => service.ServiceType == typeof(IJournalStorageProvider));
        services[services.IndexOf(descriptor)] = new ServiceDescriptor(typeof(IJournalStorageProvider), provider =>
        {
            var inner = (IJournalStorageProvider)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
            return new BudgetedJournalStorageProvider(inner, provider.GetRequiredService<NeuronOptions>().StorageOperationBudget);
        }, descriptor.Lifetime);
        return builder;
    }
}
