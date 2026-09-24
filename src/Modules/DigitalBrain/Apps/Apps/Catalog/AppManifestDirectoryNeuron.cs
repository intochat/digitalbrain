using DigitalBrain.Apps.Manifests;
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

// The app directory. It always exposes the committed first-party manifests as global entries and the
// latest saved or installed version of every catalogued app, keyed by app id and carrying the owning
// workspace so Discovery can keep one workspace's content out of another's search.
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

    public Task<IReadOnlyList<ScopedAppManifest>> Read()
    {
        var merged = new Dictionary<string, ScopedAppManifest>(StringComparer.Ordinal);
        foreach (var manifest in FirstPartyApps.All()) { merged[manifest.Id] = ScopedAppManifest.Global(manifest); }
        foreach (var (id, scoped) in Snapshot.Manifests) { merged[id] = scoped; }
        return Task.FromResult<IReadOnlyList<ScopedAppManifest>>([.. merged.Values]);
    }
}
