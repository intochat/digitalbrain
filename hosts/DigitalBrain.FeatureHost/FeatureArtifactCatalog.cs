using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.FeatureHost;

internal sealed record FeatureArtifactInstallation(
    BrainOwnerId OwnerId,
    ActorId ActorId,
    FeatureInstallationId InstallationId,
    GrantRevision GrantRevision,
    IReadOnlyDictionary<string, ProviderConnectionId> ProviderConnections,
    FeatureReleaseDescriptor Release);
internal interface IFeatureArtifactCatalog
{
    ValueTask<IReadOnlyList<FeatureArtifactInstallation>> ReadActiveAsync(CancellationToken cancellationToken = default);
}
internal sealed class BlobFeatureArtifactCatalog : IFeatureArtifactCatalog
{
    private const string ContainerName = "feature-releases";
    private const int MaximumActiveInstallations = 1_024;
    private const int MaximumManifestBytes = 65_536;
    private const int MaximumReleaseFiles = 256;
    private const long MaximumReleaseBytes = 67_108_864;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly BlobContainerClient _container;
    private readonly string _cacheRoot;
    private readonly SemaphoreSlim _materialization = new(1, 1);
    public BlobFeatureArtifactCatalog(BlobServiceClient blobs, string? cacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(blobs);
        _container = blobs.GetBlobContainerClient(ContainerName);
        _cacheRoot = Path.GetFullPath(cacheDirectory ?? Path.Combine(Path.GetTempPath(), "digitalbrain-feature-artifacts"));
        Directory.CreateDirectory(_cacheRoot);
        if (File.GetAttributes(_cacheRoot).HasFlag(FileAttributes.ReparsePoint))
            throw new ArgumentException("The artifact cache cannot be a filesystem link.", nameof(cacheDirectory));
    }
    public async ValueTask<IReadOnlyList<FeatureArtifactInstallation>> ReadActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!(await _container.ExistsAsync(cancellationToken)).Value)
            return [];
        var active = new List<FeatureArtifactInstallation>();
        await foreach (var item in _container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, "active/", cancellationToken))
        {
            if (active.Count == MaximumActiveInstallations)
                throw new FeatureReleaseValidationException("The active installation catalog exceeds its bound.");
            if (!item.Name.EndsWith(".json", StringComparison.Ordinal) || item.Properties.ContentLength is null or < 2 or > MaximumManifestBytes)
                throw new FeatureReleaseValidationException("An active installation manifest is invalid.");
            await using var stream = new MemoryStream((int)item.Properties.ContentLength.Value);
            await DownloadBoundedAsync(_container.GetBlobClient(item.Name), item, stream, MaximumManifestBytes, cancellationToken);
            stream.Position = 0;
            FeatureArtifactManifest manifest;
            try
            {
                manifest = await JsonSerializer.DeserializeAsync<FeatureArtifactManifest>(stream, Json, cancellationToken) ?? throw new JsonException();
            }
            catch (JsonException exception)
            {
                throw new FeatureReleaseValidationException("An active installation manifest is invalid.", exception);
            }
            ValidatePublication(manifest);
            var owner = new BrainOwnerId(manifest.OwnerId);
            var actor = new ActorId(manifest.ActorId);
            var installation = new FeatureInstallationId(manifest.InstallationId);
            var digest = new ReleaseDigest(manifest.ReleaseDigest);
            var expectedName = $"active/{Segment(owner.Value)}/{Segment(installation.Value)}.json";
            if (!string.Equals(item.Name, expectedName, StringComparison.Ordinal))
                throw new FeatureReleaseValidationException("An active installation manifest path is not canonical.");
            var connections = (manifest.ProviderConnections ?? new Dictionary<string, string>()).ToDictionary(
                    pair => RequiredProvider(pair.Key),
                    pair => new ProviderConnectionId(pair.Value),
                    StringComparer.Ordinal);
            var releaseDirectory = await MaterializeAsync(digest, cancellationToken);
            active.Add(new FeatureArtifactInstallation(owner, actor, installation, new GrantRevision(manifest.GrantRevision), connections, new FeatureReleaseDescriptor(digest, releaseDirectory)));
        }
        return active;
    }
    private async Task<string> MaterializeAsync(ReleaseDigest digest, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(_cacheRoot, digest.Value);
        if (Directory.Exists(destination))
            return destination;
        await _materialization.WaitAsync(cancellationToken);
        try
        {
            if (Directory.Exists(destination))
                return destination;
            var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(staging);
            try
            {
                var prefix = $"releases/{digest.Value}/";
                var count = 0;
                long totalBytes = 0;
                await foreach (var item in _container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, cancellationToken))
                {
                    count++;
                    if (count > MaximumReleaseFiles || item.Properties.ContentLength is null or < 0 or > MaximumReleaseBytes)
                        throw new FeatureReleaseValidationException("The feature release exceeds its artifact bounds.");
                    var relative = item.Name[prefix.Length..];
                    var target = SafePath(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    totalBytes += await DownloadBoundedAsync(_container.GetBlobClient(item.Name), item, output, MaximumReleaseBytes - totalBytes, cancellationToken);
                }
                if (count == 0)
                    throw new FeatureReleaseValidationException("The active feature release has no artifacts.");
                Directory.Move(staging, destination);
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                throw;
            }
            return destination;
        }
        finally
        {
            _materialization.Release();
        }
    }
    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new FeatureReleaseValidationException("A feature artifact path is invalid.");
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new FeatureReleaseValidationException("A feature artifact escapes its release directory.");
        return full;
    }
    private static async Task<long> DownloadBoundedAsync(BlobClient blob, Azure.Storage.Blobs.Models.BlobItem item, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var options = new Azure.Storage.Blobs.Models.BlobDownloadOptions();
        if (item.Properties.ETag is { } etag)
        {
            options.Conditions = new Azure.Storage.Blobs.Models.BlobRequestConditions { IfMatch = etag };
        }
        using var download = (await blob.DownloadStreamingAsync(options, cancellationToken)).Value;
        if (download.Details.ContentLength > maximumBytes)
            throw new FeatureReleaseValidationException("A feature artifact exceeds its byte bound.");
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await download.Content.ReadAsync(buffer, cancellationToken);
            if (read == 0) return total;
            total += read;
            if (total > maximumBytes)
                throw new FeatureReleaseValidationException("A feature artifact exceeds its byte bound.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
    private static string RequiredProvider(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(char.IsControl))
            throw new FeatureReleaseValidationException("A provider connection key is invalid.");
        return value;
    }
    private static void ValidatePublication(FeatureArtifactManifest manifest)
    {
        var legacy = manifest.PublicationFence == 0 && manifest.AuthorityDigest is null && manifest.AccessDigest is null;
        var fenced = manifest.PublicationFence > 0 && CanonicalDigest(manifest.AuthorityDigest) && CanonicalDigest(manifest.AccessDigest);
        if (!legacy && !fenced)
            throw new FeatureReleaseValidationException("An active installation publication fence is invalid.");
    }
    private static bool CanonicalDigest(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Segment(string value) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed record FeatureArtifactManifest(
        string OwnerId,
        string ActorId,
        string InstallationId,
        string ReleaseDigest,
        long GrantRevision,
        IReadOnlyDictionary<string, string>? ProviderConnections,
        long PublicationFence = 0,
        string? AuthorityDigest = null,
        string? AccessDigest = null);
}
