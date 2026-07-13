using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Tests.Kernel;

public class AzureBlobPackConfigBackingStoreTests
{
    private static ReadOnlySpan<byte> IdentifierKeyPurpose =>
        "DigitalBrain.PackConfig.BlobIdentifierKey.v1"u8;

    [Fact]
    public async Task SaveAsync_UsesDeterministicPurposeDerivedOpaqueName()
    {
        const string scope = "tenant/alice@example.com";
        const string pack = "salesforce-private-config";
        var signingKey = Key(0x2a);
        var blobs = new InMemoryBlobServiceClient();
        var store = Store(blobs, signingKey);

        await store.SaveAsync(scope, pack, [1, 2, 3]);
        await store.SaveAsync(scope, pack, [4, 5, 6]);

        var blobName = Assert.Single(blobs.Container.Names);
        Assert.Equal(ExpectedName(signingKey, scope, pack), blobName);
        Assert.False(blobName.Contains(scope, StringComparison.Ordinal));
        Assert.False(blobName.Contains(pack, StringComparison.Ordinal));
        Assert.Equal(new byte[] { 4, 5, 6 }, blobs.Container.Read(blobName));
    }

    [Fact]
    public async Task SaveAsync_LengthPrefixesPreventComponentBoundaryCollisions()
    {
        var blobs = new InMemoryBlobServiceClient();
        var store = Store(blobs, Key(0x2b));

        await store.SaveAsync("ab", "c", [1]);
        await store.SaveAsync("a", "bc", [2]);

        Assert.Equal(2, blobs.Container.Names.Count);
    }

    [Fact]
    public void Constructor_RejectsIncompleteStableKeyMaterial()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Store(new InMemoryBlobServiceClient(), new byte[31]));

        Assert.Contains("stable signing key material", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_transparently_migrates_a_legacy_entry_on_the_first_read()
    {
        const string scope = "tenant-secret-scope";
        const string pack = "google-private-config";
        var sourceBytes = new byte[] { 9, 8, 7, 6 };
        var signingKey = Key(0x2c);
        var blobs = new InMemoryBlobServiceClient();
        var store = Store(blobs, signingKey);
        var legacyName = $"{scope}/{pack}.bin";
        blobs.Container.Seed(legacyName, sourceBytes);

        // A pre-existing connection (persisted under the plaintext legacy name before opaque naming
        // shipped) must be visible on the very first read, not just after an explicit migration call --
        // otherwise the caller silently looks "disconnected" and is forced to re-authenticate.
        var first = await store.LoadAsync(scope, pack);
        var second = await store.LoadAsync(scope, pack);

        Assert.Equal(sourceBytes, first);
        Assert.Equal(sourceBytes, second);
        Assert.Equal(sourceBytes, blobs.Container.Read(legacyName));
        Assert.Equal(sourceBytes, blobs.Container.Read(ExpectedName(signingKey, scope, pack)));
    }

    [Fact]
    public async Task MigrateLegacyEntryAsync_MissingKnownSourceDoesNotCreateAnEntry()
    {
        var blobs = new InMemoryBlobServiceClient();
        var store = Store(blobs, Key(0x2d));

        var result = await store.MigrateLegacyEntryAsync("missing-scope", "missing-pack");

        Assert.Equal(PackConfigLegacyMigrationResult.SourceMissing, result);
        Assert.Empty(blobs.Container.Names);
    }

    [Fact]
    public async Task MigrateLegacyEntryAsync_MismatchedDestinationFailsWithoutOverwritingEitherEntry()
    {
        const string scope = "tenant-mismatch-scope";
        const string pack = "telegram-private-config";
        var sourceBytes = new byte[] { 1, 3, 5 };
        var destinationBytes = new byte[] { 2, 4, 6 };
        var signingKey = Key(0x2e);
        var blobs = new InMemoryBlobServiceClient();
        var store = Store(blobs, signingKey);
        var legacyName = $"{scope}/{pack}.bin";
        blobs.Container.Seed(legacyName, sourceBytes);
        await store.SaveAsync(scope, pack, destinationBytes);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.MigrateLegacyEntryAsync(scope, pack));

        Assert.Equal(sourceBytes, blobs.Container.Read(legacyName));
        Assert.Equal(destinationBytes, await store.LoadAsync(scope, pack));
    }

    [Fact]
    public async Task MigrateLegacyEntryAsync_VerifiesNewCopyAndLeavesSourceUntouched()
    {
        const string scope = "tenant-copy-verification-scope";
        const string pack = "copy-verification-pack";
        var sourceBytes = new byte[] { 11, 22, 33 };
        var blobs = new InMemoryBlobServiceClient();
        var store = Store(blobs, Key(0x2f));
        var legacyName = $"{scope}/{pack}.bin";
        blobs.Container.Seed(legacyName, sourceBytes);
        blobs.Container.CorruptNextWrite = true;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.MigrateLegacyEntryAsync(scope, pack));

        Assert.Equal(sourceBytes, blobs.Container.Read(legacyName));
    }

    private static AzureBlobPackConfigBackingStore Store(
        BlobServiceClient blobs,
        byte[] signingKey) =>
        new(blobs, new TestRuntimeStateKeyRing(signingKey));

    private static string ExpectedName(ReadOnlySpan<byte> signingKey, string scope, string pack)
    {
        var identifierKey = HMACSHA256.HashData(signingKey, IdentifierKeyPurpose);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteComponent(writer, scope);
        WriteComponent(writer, pack);
        writer.Flush();
        var identifier = HMACSHA256.HashData(identifierKey, stream.ToArray());
        return $"entries/{Convert.ToHexString(identifier).ToLowerInvariant()}.bin";
    }

    private static void WriteComponent(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] Key(byte value) => Enumerable.Repeat(value, 32).ToArray();

    private sealed class TestRuntimeStateKeyRing(byte[] signingKey) : IRuntimeStateKeyRing
    {
        public int ActiveKekVersion => 1;
        public ReadOnlyMemory<byte> SigningKey { get; } = signingKey;

        public bool TryGetKek(int version, out ReadOnlyMemory<byte> key)
        {
            key = default;
            return false;
        }
    }

    private sealed class InMemoryBlobServiceClient : BlobServiceClient
    {
        public InMemoryBlobContainerClient Container { get; } = new();

        public override BlobContainerClient GetBlobContainerClient(string blobContainerName) => Container;
    }

    private sealed class InMemoryBlobContainerClient : BlobContainerClient
    {
        private readonly Dictionary<string, byte[]> _contents = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Names => _contents.Keys.ToArray();
        public bool CorruptNextWrite { get; set; }

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Response<BlobContainerInfo>>(null!);
        }

        public override BlobClient GetBlobClient(string blobName) => new InMemoryBlobClient(this, blobName);

        public void Seed(string blobName, byte[] contents) => _contents[blobName] = contents.ToArray();

        public bool Contains(string blobName) => _contents.ContainsKey(blobName);

        public byte[] Read(string blobName) => _contents[blobName].ToArray();

        public void Write(string blobName, byte[] contents, bool overwrite)
        {
            if (!overwrite && _contents.ContainsKey(blobName))
            {
                throw new InvalidOperationException("The test blob already exists.");
            }

            var stored = contents.ToArray();
            if (CorruptNextWrite && stored.Length > 0)
            {
                CorruptNextWrite = false;
                stored[0] ^= 0xff;
            }

            _contents[blobName] = stored;
        }
    }

    private sealed class InMemoryBlobClient(
        InMemoryBlobContainerClient container,
        string blobName) : BlobClient
    {
        public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Response.FromValue(container.Contains(blobName), null!));
        }

        public override async Task<Response> DownloadToAsync(
            Stream destination,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(container.Read(blobName), cancellationToken);
            return null!;
        }

        public override Task<Response<BlobContentInfo>> UploadAsync(
            BinaryData content,
            bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            container.Write(blobName, content.ToArray(), overwrite);
            return Task.FromResult<Response<BlobContentInfo>>(null!);
        }
    }
}
