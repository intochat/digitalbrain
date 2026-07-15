using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DigitalBrain.FeatureHost;
using DigitalBrain.Kernel.Contracts;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class FeatureArtifactCatalogTests
{
    private const string Owner = "owner-1";
    private const string Installation = "installation-1";
    private static readonly string Digest = new('a', 64);

    [Fact]
    public async Task Materializes_only_the_selected_release_under_the_digest_cache()
    {
        using var cache = new TemporaryDirectory();
        var blobs = CatalogBlobs();
        blobs.Container.Seed($"releases/{Digest}/feature.dll", [1, 2, 3]);
        blobs.Container.Seed($"releases/{Digest}/nested/feature.deps.json", [4, 5]);
        var catalog = new BlobFeatureArtifactCatalog(blobs, cache.Path);

        var active = Assert.Single(await catalog.ReadActiveAsync());

        Assert.Equal(Owner, active.OwnerId.Value);
        Assert.Equal(Installation, active.InstallationId.Value);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(active.Release.ReleaseDirectory, "feature.dll")));
        Assert.Equal(new byte[] { 4, 5 }, await File.ReadAllBytesAsync(Path.Combine(active.Release.ReleaseDirectory, "nested", "feature.deps.json")));
        Assert.Equal(Path.Combine(cache.Path, Digest), active.Release.ReleaseDirectory);
    }

    [Fact]
    public async Task Rejects_release_blob_that_escapes_the_digest_cache()
    {
        using var cache = new TemporaryDirectory();
        var blobs = CatalogBlobs();
        blobs.Container.Seed($"releases/{Digest}/../escape.dll", [1]);
        var catalog = new BlobFeatureArtifactCatalog(blobs, cache.Path);

        await Assert.ThrowsAsync<FeatureReleaseValidationException>(async () =>
            await catalog.ReadActiveAsync());

        Assert.False(File.Exists(Path.Combine(cache.Path, "escape.dll")));
    }

    [Fact]
    public async Task Rejects_noncanonical_active_manifest_path()
    {
        using var cache = new TemporaryDirectory();
        var blobs = new InMemoryBlobServiceClient();
        blobs.Container.Seed("active/owner/installation.json", Manifest());
        var catalog = new BlobFeatureArtifactCatalog(blobs, cache.Path);

        await Assert.ThrowsAsync<FeatureReleaseValidationException>(async () =>
            await catalog.ReadActiveAsync());
    }

    [Fact]
    public async Task Rejects_manifest_when_download_is_larger_than_listing_metadata()
    {
        using var cache = new TemporaryDirectory();
        var blobs = new InMemoryBlobServiceClient();
        blobs.Container.Seed(
            $"active/{Segment(Owner)}/{Segment(Installation)}.json",
            new byte[70 * 1024],
            listedLength: 2);
        var catalog = new BlobFeatureArtifactCatalog(blobs, cache.Path);

        await Assert.ThrowsAsync<FeatureReleaseValidationException>(async () =>
            await catalog.ReadActiveAsync());
    }

    [Fact]
    public async Task Accepts_a_fenced_active_manifest_without_changing_the_runtime_projection()
    {
        using var cache = new TemporaryDirectory();
        var blobs = new InMemoryBlobServiceClient();
        blobs.Container.Seed(
            $"active/{Segment(Owner)}/{Segment(Installation)}.json",
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                ownerId = Owner,
                actorId = "actor-1",
                installationId = Installation,
                releaseDigest = Digest,
                grantRevision = 1,
                providerConnections = new Dictionary<string, string>(),
                publicationFence = 7,
                authorityDigest = new string('b', 64),
                accessDigest = new string('c', 64)
            }));
        blobs.Container.Seed($"releases/{Digest}/feature.dll", [1]);
        var catalog = new BlobFeatureArtifactCatalog(blobs, cache.Path);

        var active = Assert.Single(await catalog.ReadActiveAsync());

        Assert.Equal(Owner, active.OwnerId.Value);
        Assert.Equal(Installation, active.InstallationId.Value);
        Assert.Equal(new GrantRevision(1), active.GrantRevision);
    }

    private static InMemoryBlobServiceClient CatalogBlobs()
    {
        var blobs = new InMemoryBlobServiceClient();
        blobs.Container.Seed(
            $"active/{Segment(Owner)}/{Segment(Installation)}.json",
            Manifest());
        return blobs;
    }

    private static byte[] Manifest() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        ownerId = Owner,
        actorId = "actor-1",
        installationId = Installation,
        releaseDigest = Digest,
        grantRevision = 1,
        providerConnections = new Dictionary<string, string>()
    });

    private static string Segment(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class InMemoryBlobServiceClient : BlobServiceClient
    {
        public InMemoryBlobContainerClient Container { get; } = new();

        public override BlobContainerClient GetBlobContainerClient(string blobContainerName) => Container;
    }

    private sealed class InMemoryBlobContainerClient : BlobContainerClient
    {
        private readonly Dictionary<string, BlobEntry> contents = new(StringComparer.Ordinal);

        public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Response.FromValue(true, null!));

        public override BlobClient GetBlobClient(string blobName) => new InMemoryBlobClient(contents, blobName);

        public override AsyncPageable<BlobItem> GetBlobsAsync(
            BlobTraits traits = BlobTraits.None,
            BlobStates states = BlobStates.None,
            string? prefix = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = contents
                .Where(pair => pair.Key.StartsWith(prefix ?? string.Empty, StringComparison.Ordinal))
                .Select(pair => BlobsModelFactory.BlobItem(
                    pair.Key,
                    properties: BlobsModelFactory.BlobItemProperties(
                        accessTierInferred: false,
                        contentLength: pair.Value.ListedLength,
                        eTag: pair.Value.ETag)))
                .ToArray();
            return AsyncPageable<BlobItem>.FromPages([
                Page<BlobItem>.FromValues(items, null, null!)
            ]);
        }

        public void Seed(string name, byte[] value, long? listedLength = null) =>
            contents.Add(name, new BlobEntry(
                value,
                listedLength ?? value.LongLength,
                new ETag($"\"{Guid.NewGuid():N}\"")));
    }

    private sealed class InMemoryBlobClient(
        IReadOnlyDictionary<string, BlobEntry> contents,
        string name) : BlobClient
    {
        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions options = null!,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = contents[name];
            Assert.Equal(entry.ETag, options.Conditions.IfMatch);
            var result = BlobsModelFactory.BlobDownloadStreamingResult(
                new MemoryStream(entry.Content, writable: false),
                BlobsModelFactory.BlobDownloadDetails(
                    contentLength: entry.Content.LongLength,
                    eTag: entry.ETag));
            return Task.FromResult(Response.FromValue(result, null!));
        }
    }

    private sealed record BlobEntry(byte[] Content, long ListedLength, ETag ETag);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"digitalbrain-catalog-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
