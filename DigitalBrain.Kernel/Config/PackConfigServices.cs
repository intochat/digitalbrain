using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Core.Config;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Kernel.Config;

public static class PackConfigServices
{
    // Registers IPackConfigStore.
    // Pass blobsForKeyRing (Aspire-hosted path) to share the DataProtection key ring across all replicas via
    // blob storage — without it (integration tests, fast path) each process gets an ephemeral key ring.
    public static IServiceCollection AddPackConfigStore(
        this IServiceCollection services,
        BlobServiceClient? blobsForKeyRing = null)
    {
        var dp = services.AddDataProtection()
            .SetApplicationName("DigitalBrain.PackConfig");

        if (blobsForKeyRing is not null)
        {
            var container = blobsForKeyRing.GetBlobContainerClient("pack-config");
            // Unlike AzureBlobPackConfigBackingStore.SaveAsync (which ensure-creates this same container
            // lazily on first pack-config write), DataProtection's AzureBlobXmlRepository uploads the key
            // ring directly with no such check and throws ContainerNotFound on a fresh Azurite/Storage
            // account - fails the first grain activation that needs a protector, before anything ever
            // wrote a pack config.
            container.CreateIfNotExists(PublicAccessType.None);
            dp.PersistKeysToAzureBlobStorage(container.GetBlobClient("dp-keys/keys.xml"));
        }

        services.AddSingleton<IPackConfigBackingStore>(sp =>
        {
            var blobs = sp.GetService<BlobServiceClient>();
            if (blobs is not null)
                return new AzureBlobPackConfigBackingStore(blobs);
            return new InMemoryPackConfigBackingStore();
        });

        services.AddSingleton<IPackConfigStore, PackConfigStore>();
        return services;
    }
}
