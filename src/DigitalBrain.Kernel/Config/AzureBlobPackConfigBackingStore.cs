using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DigitalBrain.Kernel.Config;

// Prod backing: persists each (scope, pack) blob as a block blob in the pack-config container.
// The container is auto-created on first write. The BlobServiceClient is injected from the Aspire
// connection string (same Azurite instance as grain storage).
public sealed class AzureBlobPackConfigBackingStore(BlobServiceClient blobs) : IPackConfigBackingStore
{
    private const string ContainerName = "pack-config";

    private BlobClient EntryBlob(string scope, string pack)
        => blobs.GetBlobContainerClient(ContainerName).GetBlobClient($"{scope}/{pack}.bin");

    public async Task<byte[]?> LoadAsync(string scope, string pack, CancellationToken cancellationToken = default)
    {
        var blob = EntryBlob(scope, pack);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        using var stream = new MemoryStream();
        await blob.DownloadToAsync(stream, cancellationToken);
        return stream.ToArray();
    }

    public async Task SaveAsync(string scope, string pack, byte[] encryptedBlob, CancellationToken cancellationToken = default)
    {
        var container = blobs.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await container.GetBlobClient($"{scope}/{pack}.bin")
            .UploadAsync(new BinaryData(encryptedBlob), overwrite: true, cancellationToken);
    }
}
