using Azure.Storage.Blobs;

namespace DigitalBrain.UI;

internal sealed class BlobTurnContextStore(BlobServiceClient blobs) : ITurnContextBlobStore
{
    private const string ContainerName = "turn-context";

    public async Task<string> SaveAsync(string digest, string payloadJson, CancellationToken cancellationToken)
    {
        var container = blobs.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await container.GetBlobClient(digest)
            .UploadAsync(BinaryData.FromString(payloadJson), overwrite: true, cancellationToken).ConfigureAwait(false);
        return digest;
    }

    public async Task<string?> ReadAsync(string blobRef, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(ContainerName).GetBlobClient(blobRef);
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return download.Value.Content.ToString();
    }
}
