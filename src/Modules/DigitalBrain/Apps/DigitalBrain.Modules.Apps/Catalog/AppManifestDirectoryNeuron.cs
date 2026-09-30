using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps.Catalog;

[GenerateSerializer, Alias("apps.directory-state")]
internal sealed record AppManifestDirectoryState
{
    // Scoped entries live at Id 1. The P2.6b unscoped dictionary at Id 0 is abandoned (T1 clean break
    // before the first design partner); leaving Id 0 unused lets a stale blob deserialize instead of
    // throwing, and any pre-scope entry is simply not re-catalogued.
    [Id(1)] public Dictionary<string, ScopedAppManifest> Manifests { get; init; } = new(StringComparer.Ordinal);
}

// The app directory: the latest saved or installed version of every catalogued app, keyed by app id
// and carrying the owning workspace so callers can keep one workspace's content out of another's
// results.
[GrainType("app-manifest-directory")]
internal sealed class AppManifestDirectoryNeuron(
    [PersistentState("directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppManifestDirectoryState> store)
    : Neuron<AppManifestDirectoryState>(store), IAppManifestDirectory
{
    public async Task<AppManifest> Publish(AppManifest manifest, string workspaceId)
    {
        ManifestValidator.Validate(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var next = new Dictionary<string, ScopedAppManifest>(Snapshot.Manifests, StringComparer.Ordinal)
        {
            [manifest.Id] = ScopedAppManifest.InWorkspace(manifest, workspaceId),
        };
        await Save(new AppManifestDirectoryState { Manifests = next }, new AppCatalogued(manifest.Id, manifest.Version, DateTimeOffset.UtcNow));
        return manifest;
    }

    public Task<IReadOnlyList<ScopedAppManifest>> Read() =>
        Task.FromResult<IReadOnlyList<ScopedAppManifest>>([.. Snapshot.Manifests.Values]);
}
