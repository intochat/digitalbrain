using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

public sealed record InstalledAppSummary(string Id, PackageId Package, string Title, string Description, AppSnapshot App);

public static class InstalledApps
{
    public static Task<IReadOnlyList<InstalledAppSummary>> List(IDigitalBrain brain, string scope, CancellationToken ct)
        => List(brain.Get<IApps>(scope), id => brain.Get<IApp>(scope + "/packages/" + id),
            id => brain.Get<IPackage>(id.ToString()), ct);

    public static Task<IReadOnlyList<InstalledAppSummary>> List(IGrainFactory grains, string scope, CancellationToken ct)
        => List(grains.GetGrain<IApps>(scope), id => grains.GetGrain<IApp>(scope + "/packages/" + id),
            id => grains.GetGrain<IPackage>(id.ToString()), ct);

    private static async Task<IReadOnlyList<InstalledAppSummary>> List(IApps apps,
        Func<PackageId, IApp> app, Func<PackageId, IPackage> package, CancellationToken ct)
    {
        var installed = new List<InstalledAppSummary>();
        foreach (var id in (await apps.List().WaitAsync(ct)).OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            var snapshot = await app(id).Read().WaitAsync(ct);
            if (snapshot.Status != AppStatus.Installed || snapshot.UninstallPending || snapshot.Revision is not { } revision) { continue; }
            var manifest = (await package(id).ReadRevision(revision.Revision).WaitAsync(ct)).Content.Manifest;
            installed.Add(new(id.ToString(), id, manifest.Title, manifest.Description, snapshot));
        }
        return installed;
    }
}
