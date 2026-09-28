using System.Security.Cryptography;
using Azure;
using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace IntoChat.LocalFiles;

internal interface IAssetBlobStore
{
    Task Put(string id, byte[] bytes, CancellationToken ct);
    Task<byte[]?> Read(string id, CancellationToken ct);
}

// Binary payloads are immutable. Only workspace neurons decide which payloads are published.
internal sealed class AzureAssetBlobStore([FromKeyedServices(DigitalBrainNames.GrainState)] BlobServiceClient client) : IAssetBlobStore
{
    internal const string ContainerName = "intochat-assets-v1";
    public async Task Put(string id, byte[] bytes, CancellationToken ct)
    {
        var container = client.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        try { await container.GetBlobClient(id).UploadAsync(BinaryData.FromBytes(bytes), overwrite: false, cancellationToken: ct); }
        catch (RequestFailedException error) when (error.Status is 409 or 412)
        {
            var existing = await Read(id, ct);
            if (existing is null || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), SHA256.HashData(existing)))
            { throw new IOException("The immutable asset has conflicting content."); }
        }
    }

    public async Task<byte[]?> Read(string id, CancellationToken ct)
    {
        try
        {
            var blob = client.GetBlobContainerClient(ContainerName).GetBlobClient(id);
            if ((await blob.GetPropertiesAsync(cancellationToken: ct)).Value.ContentLength > LocalFileStore.MaxExportBytes)
            { throw new IOException("The stored asset exceeds the supported size."); }
            return (await blob.DownloadContentAsync(ct)).Value.Content.ToArray();
        }
        catch (RequestFailedException error) when (error.Status == 404) { return null; }
    }
}
