using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.UI;

internal sealed class BlobKitImageStore([FromKeyedServices(DigitalBrainNames.GrainState)] BlobServiceClient blobs)
    : IKitImageStore
{
    internal const string ContainerName = "kit-images";

    public async Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string mediaType, CancellationToken cancellationToken)
    {
        var container = blobs.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await container.GetBlobClient(blobName)
            .UploadAsync(
                new BinaryData(content),
                new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = mediaType } },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(byte[] Content, string MediaType)?> ReadAsync(string blobName, CancellationToken cancellationToken)
    {
        var blob = blobs.GetBlobContainerClient(ContainerName).GetBlobClient(blobName);
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return (download.Value.Content.ToArray(), download.Value.Details.ContentType ?? "application/octet-stream");
    }
}
