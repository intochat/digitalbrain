using DigitalBrain.Apps.Manifests;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps.Catalog;

[GenerateSerializer, Alias("apps.directory-state")]
internal sealed record AppManifestDirectoryState
{
    [Id(0)] public Dictionary<string, AppManifest> Manifests { get; init; } = new(StringComparer.Ordinal);
}

// The global manifest directory. It always exposes the committed first-party manifests and the
// latest saved or installed version of every catalogued app, keyed by app id.
[GrainType("app-manifest-directory")]
internal sealed class AppManifestDirectoryNeuron(
    [PersistentState("directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppManifestDirectoryState> store)
    : Neuron<AppManifestDirectoryState>(store), IAppManifestDirectory
{
    public async Task<AppManifest> Publish(AppManifest manifest)
    {
        ManifestValidator.Validate(manifest);
        var next = new Dictionary<string, AppManifest>(Snapshot.Manifests, StringComparer.Ordinal)
        {
            [manifest.Id] = manifest,
        };
        await Save(new AppManifestDirectoryState { Manifests = next }, new AppCatalogued(manifest.Id, manifest.Version, DateTimeOffset.UtcNow));
        return manifest;
    }

    public Task<IReadOnlyList<AppManifest>> Read()
    {
        var merged = new Dictionary<string, AppManifest>(StringComparer.Ordinal);
        foreach (var manifest in FirstPartyApps.All()) { merged[manifest.Id] = manifest; }
        foreach (var (id, manifest) in Snapshot.Manifests) { merged[id] = manifest; }
        return Task.FromResult<IReadOnlyList<AppManifest>>([.. merged.Values]);
    }
}
