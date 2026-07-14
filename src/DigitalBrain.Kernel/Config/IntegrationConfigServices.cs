using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Contracts.Configuration;
using DigitalBrain.Kernel.Runtime;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Kernel.Config;

internal static class IntegrationConfigServices
{

    public static IServiceCollection AddIntegrationConfigStore(this IServiceCollection services, BlobServiceClient? blobsForKeyRing = null)
    {
        var dp = services.AddDataProtection().SetApplicationName("DigitalBrain.IntegrationConfig");

        if (blobsForKeyRing is not null)
        {
            var container = blobsForKeyRing.GetBlobContainerClient("pack-config");

            container.CreateIfNotExists(PublicAccessType.None);
            dp.PersistKeysToAzureBlobStorage(container.GetBlobClient("dp-keys/keys.xml"));
        }

        services.AddSingleton<IIntegrationConfigBackingStore>(serviceProvider => blobsForKeyRing is not null
                    ? new AzureBlobIntegrationConfigBackingStore(blobsForKeyRing, serviceProvider.GetRequiredService<IRuntimeStateKeyRing>())
                    : new InMemoryIntegrationConfigBackingStore());

        services.AddSingleton<IIntegrationConfigStore, IntegrationConfigStore>();
        return services;
    }
}
