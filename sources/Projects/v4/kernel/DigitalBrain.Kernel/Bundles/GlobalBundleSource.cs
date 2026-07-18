using DigitalBrain.Abstractions.Bundles;
using DigitalBrain.Abstractions.Distribution;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel.Bundles;

public sealed class GlobalBundleSource(
    IBundleRegistry registry,
    IEnumerable<BundleId>? bundleIds = null) : IBundleSource
{
    public IReadOnlyList<IBundle> LoadBundles()
    {
        var requested = bundleIds?.ToArray();
        var manifests = requested is { Length: > 0 }
            ? requested
                .Select(id => registry.ResolveAsync(id, BundleVersionSelector.Latest).GetAwaiter().GetResult())
                .Where(result => result.Success && result.Manifest is not null)
                .Select(result => result.Manifest!)
                .ToArray()
            : registry.ListPublishedAsync().GetAwaiter().GetResult().ToArray();

        return manifests
            .Select(manifest => registry.DownloadAsync(manifest.BundleId, manifest.Version).GetAwaiter().GetResult())
            .Where(result => result.Success && result.Bundle is not null)
            .Select(result => (IBundle)new GlobalBundleDescriptor(result.Bundle!))
            .ToArray();
    }
}

public sealed class GlobalBundleDescriptor(BundleDownload download) : IBundle
{
    public BundleId Id => download.Manifest.BundleId;

    public void Install(IServiceCollection services)
    {
        services.AddSingleton(download);
        services.AddSingleton(new GlobalBundleInstallationRecord(
            download.Manifest.BundleId,
            download.Manifest.Version,
            download.Manifest.Assets.Select(asset => asset.Hash).ToArray()));
    }
}

public sealed record GlobalBundleInstallationRecord(
    BundleId BundleId,
    BundleVersion Version,
    IReadOnlyList<AssetHash> AssetHashes);
