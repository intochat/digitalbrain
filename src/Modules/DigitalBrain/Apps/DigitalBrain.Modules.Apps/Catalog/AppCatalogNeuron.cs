using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps.Catalog;

[GenerateSerializer, Alias("apps.catalog-state")]
internal sealed record AppCatalogState
{
    [Id(0)] public Dictionary<string, List<AppInstallation>> Versions { get; init; } = [];
}

// One catalog per workspace. Versions are append-only: a reinstall of a known version is a no-op,
// a new version is appended and activated, and rollback re-activates an earlier version.
[GrainType("app-catalog")]
internal sealed class AppCatalogNeuron(
    [PersistentState("apps", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppCatalogState> store)
    : Neuron<AppCatalogState>(store), IAppCatalog
{
    public async Task<AppInstallation> Install(AppManifest manifest)
    {
        ManifestValidator.Validate(manifest);
        var versions = Versions(manifest.Id);
        if (versions.FirstOrDefault(version => version.Manifest.Version == manifest.Version) is { } existing)
        {
            return existing;
        }

        var installed = new AppInstallation
        {
            Manifest = manifest,
            InstalledAt = DateTimeOffset.UtcNow,
            Active = true,
        };
        var next = versions.Select(version => version with { Active = false }).ToList();
        next.Add(installed);
        var state = Store(manifest.Id, next);
        await Save(state, new AppInstalled(manifest.Id, manifest.Version, installed.InstalledAt));
        await CatalogueAsync(manifest);
        return installed;
    }

    public async Task<AppInstallation> Rollback(string appId)
    {
        var versions = Versions(appId);
        var currentIndex = versions.FindLastIndex(version => version.Active);
        if (currentIndex < 0) { throw new KeyNotFoundException($"App '{appId}' is not installed."); }
        if (currentIndex == 0) { throw new InvalidOperationException($"App '{appId}' has no earlier version to roll back to."); }
        var previous = versions[currentIndex - 1];
        var next = versions
            .Select(version => version with { Active = version.Manifest.Version == previous.Manifest.Version })
            .ToList();
        await Save(Store(appId, next), new AppRolledBack(appId, previous.Manifest.Version));
        await CatalogueAsync(previous.Manifest);
        return previous;
    }

    public async Task<AppUninstallOutcome> Uninstall(string appId)
    {
        var versions = Snapshot.Versions.TryGetValue(appId, out var stored) ? stored : null;
        var active = versions?.LastOrDefault(version => version.Active);
        if (active is null) { throw new KeyNotFoundException($"App '{appId}' is not installed."); }

        var manifest = active.Manifest;
        var kept = manifest.Permissions.Select(permission => permission.SemanticTypeId)
            .Distinct(StringComparer.Ordinal)
            .Append(manifest.Id + ":data")
            .ToArray();
        var removed = new List<string> { manifest.Id + ":install" };
        if (manifest.UiEntry is { Length: > 0 } entry) { removed.Add(entry); }

        var next = new Dictionary<string, List<AppInstallation>>(Snapshot.Versions, StringComparer.Ordinal);
        next.Remove(appId);
        var state = new AppCatalogState { Versions = next };
        await Save(state, new AppUninstalled(appId, kept));
        return new AppUninstallOutcome { AppId = appId, KeptData = kept, RemovedData = removed };
    }

    public Task<IReadOnlyList<AppInstallation>> List()
    {
        IReadOnlyList<AppInstallation> active = [.. Snapshot.Versions.Values
            .Select(versions => versions.LastOrDefault(version => version.Active))
            .Where(installation => installation is not null)
            .Select(installation => installation!)];
        return Task.FromResult(active);
    }

    public Task<AppInstallation?> Read(string appId)
    {
        var active = Snapshot.Versions.TryGetValue(appId, out var versions)
            ? versions.LastOrDefault(version => version.Active)
            : null;
        return Task.FromResult(active);
    }

    private Task CatalogueAsync(AppManifest manifest) =>
        GrainFactory.GetGrain<IAppManifestDirectory>(AppManifestDirectoryGrains.Key)
            .Publish(manifest, this.GetPrimaryKeyString());

    private List<AppInstallation> Versions(string appId) =>
        Snapshot.Versions.TryGetValue(appId, out var versions) ? [.. versions] : [];

    private AppCatalogState Store(string appId, List<AppInstallation> versions)
    {
        var next = new Dictionary<string, List<AppInstallation>>(Snapshot.Versions, StringComparer.Ordinal)
        {
            [appId] = versions,
        };
        return new AppCatalogState { Versions = next };
    }
}
