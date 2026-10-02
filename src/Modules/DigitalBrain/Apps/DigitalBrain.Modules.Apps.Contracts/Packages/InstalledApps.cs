using DigitalBrain.Contracts;
using DigitalBrain.Registry;

namespace DigitalBrain.Apps;

public sealed record InstalledAppSummary(string Id, PackageId Package, string Title, string Description, AppSnapshot App);

public static class InstalledApps
{
    public static Task<IReadOnlyList<InstalledAppSummary>> List(IDigitalBrain brain, string scope, CancellationToken ct)
        => List(brain.Get<IApps>(scope), brain.Get<IPackageDirectory>(PackageDirectory.Key), () => brain.Get<IRegistry>(IRegistry.Key), scope, id => brain.Get<IApp>(scope + "/packages/" + id),
            id => brain.Get<IPackage>(id.ToString()), ct);

    public static Task<IReadOnlyList<InstalledAppSummary>> List(IGrainFactory grains, string scope, CancellationToken ct)
        => List(grains.GetGrain<IApps>(scope), grains.GetGrain<IPackageDirectory>(PackageDirectory.Key), () => grains.GetGrain<IRegistry>(IRegistry.Key), scope, id => grains.GetGrain<IApp>(scope + "/packages/" + id),
            id => grains.GetGrain<IPackage>(id.ToString()), ct);

    private static async Task<IReadOnlyList<InstalledAppSummary>> List(IApps apps, IPackageDirectory directory, Func<IRegistry> registry, string scope,
        Func<PackageId, IApp> app, Func<PackageId, IPackage> package, CancellationToken ct)
    {
        var candidates = (await apps.List().WaitAsync(ct)).ToHashSet();
        candidates.UnionWith((await directory.List().WaitAsync(ct)).Select(listing => listing.Package));
        // Discover installations created before the per-brain index existed. New installations
        // are tracked synchronously by IApp; every historical candidate is checked against it.
        try
        {
            var prefix = scope + "/packages/";
            for (var skip = 0; ; skip += 1000)
            {
                var instances = await registry().Instances("apps.app", skip: skip, take: 1000).WaitAsync(ct);
                foreach (var instance in instances.Where(instance => instance.Key.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    var parts = instance.Key[prefix.Length..].Split('/');
                    if (parts.Length == 2) { candidates.Add(PackageId.Create(parts[0], parts[1])); }
                }
                if (instances.Count < 1000) { break; }
            }
        }
        catch (ArgumentException) { } // A host may compose Apps without Registry.
        var installed = new List<InstalledAppSummary>();
        foreach (var id in candidates.OrderBy(id => id.ToString(), StringComparer.Ordinal))
        {
            var snapshot = await app(id).Read().WaitAsync(ct);
            if (snapshot.Status != AppStatus.Installed || snapshot.UninstallPending || snapshot.Revision is not { } revision) { continue; }
            var manifest = (await package(id).ReadRevision(revision.Revision).WaitAsync(ct)).Content.Manifest;
            installed.Add(new(id.ToString(), id, manifest.Title, manifest.Description, snapshot));
        }
        return installed;
    }
}
