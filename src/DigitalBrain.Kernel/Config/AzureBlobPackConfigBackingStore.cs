using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Kernel.Config;

public enum PackConfigLegacyMigrationResult
{
    SourceMissing,
    Copied,
    AlreadyMigrated
}

public sealed class AzureBlobPackConfigBackingStore : IPackConfigBackingStore
{
    private const string ContainerName = "pack-config";
    private const string EntryPrefix = "entries/";
    private static ReadOnlySpan<byte> IdentifierKeyPurpose =>
        "DigitalBrain.PackConfig.BlobIdentifierKey.v1"u8;

    private readonly BlobServiceClient _blobs;
    private readonly byte[] _identifierKey;

    public AzureBlobPackConfigBackingStore(BlobServiceClient blobs, IRuntimeStateKeyRing keys)
    {
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        ArgumentNullException.ThrowIfNull(keys);

        var signingKey = keys.SigningKey;
        if (signingKey.Length < 32)
        {
            throw new ArgumentException(
                "Durable pack-config storage requires stable signing key material of at least 256 bits.",
                nameof(keys));
        }

        _identifierKey = HMACSHA256.HashData(signingKey.Span, IdentifierKeyPurpose);
    }

    public async Task<byte[]?> LoadAsync(
        string scope,
        string pack,
        CancellationToken cancellationToken = default)
    {
        var blob = EntryBlob(scope, pack);
        if (!(await blob.ExistsAsync(cancellationToken)).Value)
        {
            return null;
        }

        return await DownloadAsync(blob, cancellationToken);
    }

    public async Task SaveAsync(
        string scope,
        string pack,
        byte[] encryptedBlob,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encryptedBlob);

        var container = Container();
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await container.GetBlobClient(EntryName(scope, pack))
            .UploadAsync(new BinaryData(encryptedBlob), overwrite: true, cancellationToken);
    }

    public async Task<PackConfigLegacyMigrationResult> MigrateLegacyEntryAsync(
        string scope,
        string pack,
        CancellationToken cancellationToken = default)
    {
        var container = Container();
        var destination = container.GetBlobClient(EntryName(scope, pack));
        var source = container.GetBlobClient($"{scope}/{pack}.bin");

        if (!(await source.ExistsAsync(cancellationToken)).Value)
        {
            return PackConfigLegacyMigrationResult.SourceMissing;
        }

        var sourceBytes = await DownloadAsync(source, cancellationToken);
        if ((await destination.ExistsAsync(cancellationToken)).Value)
        {
            var destinationBytes = await DownloadAsync(destination, cancellationToken);
            VerifyMigration(sourceBytes, destinationBytes);
            return PackConfigLegacyMigrationResult.AlreadyMigrated;
        }

        var copied = true;
        try
        {
            await destination.UploadAsync(new BinaryData(sourceBytes), overwrite: false, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            copied = false;
        }

        var copiedBytes = await DownloadAsync(destination, cancellationToken);
        VerifyMigration(sourceBytes, copiedBytes);
        return copied
            ? PackConfigLegacyMigrationResult.Copied
            : PackConfigLegacyMigrationResult.AlreadyMigrated;
    }

    private BlobContainerClient Container() => _blobs.GetBlobContainerClient(ContainerName);

    private BlobClient EntryBlob(string scope, string pack) =>
        Container().GetBlobClient(EntryName(scope, pack));

    private string EntryName(string scope, string pack)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(pack);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteIdentifierComponent(writer, scope);
        WriteIdentifierComponent(writer, pack);
        writer.Flush();

        var identifier = HMACSHA256.HashData(_identifierKey, stream.ToArray());
        return $"{EntryPrefix}{Convert.ToHexString(identifier).ToLowerInvariant()}.bin";
    }

    private static void WriteIdentifierComponent(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static async Task<byte[]> DownloadAsync(
        BlobClient blob,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await blob.DownloadToAsync(stream, cancellationToken);
        return stream.ToArray();
    }

    private static void VerifyMigration(ReadOnlySpan<byte> source, ReadOnlySpan<byte> destination)
    {
        if (source.Length != destination.Length ||
            !CryptographicOperations.FixedTimeEquals(source, destination))
        {
            throw new InvalidDataException(
                "The opaque pack-config entry does not match the legacy entry; migration stopped without overwriting either entry.");
        }
    }
}
