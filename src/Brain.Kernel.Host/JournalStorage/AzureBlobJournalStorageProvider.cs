using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Orleans.Journaling;

namespace Brain.Kernel.Host.JournalStorage;

public sealed class AzureBlobJournalStorageProvider : IJournalStorageProvider
{
    private readonly BlobContainerClient _container;
    private readonly AzureBlobJournalStorageOptions _options;

    public AzureBlobJournalStorageProvider(AzureBlobJournalStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ContainerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.JournalFormatKey);

        _options = options;
        _container = new BlobServiceClient(options.ConnectionString).GetBlobContainerClient(options.ContainerName);
    }

    public IJournalStorage CreateStorage(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        var blobName = ToBlobName(journalId);
        return new AzureBlobJournalStorage(_container.GetBlobClient(blobName), _options);
    }

    internal static string ToBlobName(JournalId journalId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(journalId.Value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
