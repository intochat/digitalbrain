using System.Security.Cryptography;
using DigitalBrain.Core.Synapses;
using DigitalBrain.Abstractions.Bundles;

namespace DigitalBrain.Abstractions.Distribution;

[GenerateSerializer]
public readonly record struct AssetPath
{
    public AssetPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Asset path must be a non-empty value.", nameof(value));
        }

        Value = value.Trim().Replace('\\', '/');
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}

[GenerateSerializer]
public readonly record struct AssetHash
{
    public const string Algorithm = "sha256";

    public AssetHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Asset hash must be a non-empty value.", nameof(value));
        }

        Value = value.Trim().ToLowerInvariant();
    }

    [Id(0)]
    public string Value { get; }

    public static AssetHash FromBytes(byte[] content) =>
        new(Convert.ToHexString(SHA256.HashData(content)));

    public override string ToString() => Value;
}

[GenerateSerializer]
public sealed record BundleSignature([property: Id(0)] byte[] Value);

[GenerateSerializer]
public readonly record struct BundleContentAddress(
    [property: Id(0)] BundleId BundleId,
    [property: Id(1)] BundleVersion Version,
    [property: Id(2)] AssetPath Path,
    [property: Id(3)] AssetHash Hash);

[GenerateSerializer]
public readonly record struct BundleVersionSelector
{
    private BundleVersionSelector(BundleVersion? version, bool isLatest)
    {
        Version = version;
        IsLatest = isLatest;
    }

    [Id(0)]
    public BundleVersion? Version { get; }

    [Id(1)]
    public bool IsLatest { get; }

    public static BundleVersionSelector Latest { get; } = new(null, true);

    public static BundleVersionSelector Exact(BundleVersion version) => new(version, false);
}

[GenerateSerializer]
public sealed record BundleAssetContent(
    [property: Id(0)] AssetPath Path,
    [property: Id(1)] byte[] Content,
    [property: Id(2)] AssetHash? DeclaredHash = null);

[GenerateSerializer]
public sealed record PublishedBundleAsset(
    [property: Id(0)] AssetPath Path,
    [property: Id(1)] AssetHash Hash,
    [property: Id(2)] long Length);

[GenerateSerializer]
public sealed record PublishedBundleManifest(
    [property: Id(0)] BundleId BundleId,
    [property: Id(1)] BundleVersion Version,
    [property: Id(2)] string DisplayName,
    [property: Id(3)] string RawJson,
    [property: Id(4)] IReadOnlyList<PublishedBundleAsset> Assets);

[GenerateSerializer]
public sealed record BundlePublishRequest(
    [property: Id(0)] string ManifestJson,
    [property: Id(1)] IReadOnlyList<BundleAssetContent> Assets,
    [property: Id(2)] BundleSignature Signature,
    [property: Id(3)] byte[] PublisherPublicKey);

[GenerateSerializer]
public sealed record BundlePublishResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] PublishedBundleManifest? Manifest,
    [property: Id(2)] IReadOnlyList<string> Diagnostics);

[GenerateSerializer]
public sealed record BundleResolveResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] PublishedBundleManifest? Manifest,
    [property: Id(2)] IReadOnlyList<string> Diagnostics);

[GenerateSerializer]
public sealed record BundleDownload(
    [property: Id(0)] PublishedBundleManifest Manifest,
    [property: Id(1)] IReadOnlyList<BundleAssetContent> Assets,
    [property: Id(2)] BundleSignature Signature,
    [property: Id(3)] byte[] PublisherPublicKey);

[GenerateSerializer]
public sealed record BundleDownloadResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] BundleDownload? Bundle,
    [property: Id(2)] IReadOnlyList<string> Diagnostics);

[GenerateSerializer]
public sealed record BundleRegistryRecord(
    [property: Id(0)] PublishedBundleManifest Manifest,
    [property: Id(1)] BundleSignature Signature,
    [property: Id(2)] byte[] PublisherPublicKey);

public interface IBundleRegistryCatalogStore
{
    Task<bool> TrySaveAsync(
        BundleRegistryRecord record,
        CancellationToken cancellationToken = default);

    Task<BundleRegistryRecord?> LoadAsync(
        BundleId bundleId,
        BundleVersion version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BundleRegistryRecord>> ListAsync(
        BundleId? bundleId = null,
        CancellationToken cancellationToken = default);
}

public interface IBundleContentStore
{
    Task SaveAsync(
        BundleContentAddress address,
        byte[] content,
        CancellationToken cancellationToken = default);

    Task<byte[]?> LoadAsync(
        BundleContentAddress address,
        CancellationToken cancellationToken = default);
}

public interface IBundleRegistry
{
    Task<BundlePublishResult> PublishAsync(
        BundlePublishRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BundleVersion>> GetVersionsAsync(
        BundleId bundleId,
        CancellationToken cancellationToken = default);

    Task<BundleResolveResult> ResolveAsync(
        BundleId bundleId,
        BundleVersionSelector selector,
        CancellationToken cancellationToken = default);

    Task<BundleDownloadResult> DownloadAsync(
        BundleId bundleId,
        BundleVersion version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublishedBundleManifest>> ListPublishedAsync(
        CancellationToken cancellationToken = default);
}

[GenerateSerializer]
public sealed record BundlePublished(
    [property: Id(0)] BundleId BundleId,
    [property: Id(1)] BundleVersion Version) : Synapse;

[GenerateSerializer]
public sealed record BundleDownloaded(
    [property: Id(0)] BundleId BundleId,
    [property: Id(1)] BundleVersion Version) : Synapse;
