using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Core.Config;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Kernel.Config;

public static class PackConfigServices
{
    // Registers IPackConfigStore.
    // Pass blobsForKeyRing (Aspire-hosted path) to share both the DataProtection key ring and the pack-config
    // backing store across all replicas via blob storage — without it (integration tests, fast path) each
    // process gets an ephemeral key ring and in-memory (non-durable, non-shared) pack config.
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

        // Reuse the explicitly-passed client (never resolve BlobServiceClient from DI here): Aspire's
        // AzureComponent.AddClient registers an unkeyed null-sentinel for BlobServiceClient whenever ANY
        // keyed registration for that type exists anywhere in the app (Program.cs keys one for grain
        // storage) - so sp.GetService<BlobServiceClient>() always returns null in the real Aspire-hosted
        // kernel, silently falling back to the in-memory backing store even in production.
        services.AddSingleton<IPackConfigBackingStore>(_ => blobsForKeyRing is not null
            ? new AzureBlobPackConfigBackingStore(blobsForKeyRing)
            : new InMemoryPackConfigBackingStore());

        services.AddSingleton<IPackConfigStore, PackConfigStore>();
        return services;
    }
}
