using DigitalBrain;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Registry;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GenerateSerializer]
internal sealed record InstalledAppsState
{
    [Id(0)] public PackageId[] Packages { get; init; } = [];
    [Id(1)] public bool Discovered { get; init; }
}

[GrainType("apps.installed")]
internal sealed class Apps(
    [PersistentState("installed-apps", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<InstalledAppsState> store,
    ModuleInventory modules)
    : Neuron<InstalledAppsState>(store), IApps
{
    public async Task<PackageId[]> List()
    {
        if (!Snapshot.Discovered) { await Discover(); }
        return Snapshot.Packages.ToArray();
    }

    private async Task Discover()
    {
        // Backfill installations predating this index once. Normal reads never scan the
        // marketplace or activation registry; IApp tracks every new installation itself.
        var prefix = this.GetPrimaryKeyString() + "/packages/";
        var candidates = new HashSet<PackageId>();
        if (modules.Types.Any(module => module.FullName == "DigitalBrain.Registry.RegistryModule"))
        {
            var registry = GrainFactory.GetGrain<IRegistry>(IRegistry.Key);
            for (var skip = 0; ; skip += 1000)
            {
                var instances = await registry.Instances("apps.app", skip: skip, take: 1000);
                foreach (var instance in instances.Where(instance => instance.Key.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    var parts = instance.Key[prefix.Length..].Split('/');
                    if (parts.Length == 2) { candidates.Add(PackageId.Create(parts[0], parts[1])); }
                }
                if (instances.Count < 1000) { break; }
            }
        }
        var tracked = Snapshot.Packages.ToHashSet();
        // Candidates are filtered by InstalledApps outside this grain, avoiding an Install/Track call cycle.
        tracked.UnionWith(candidates);
        var next = Snapshot with { Packages = tracked.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray(), Discovered = true };
        await Save(next, new AppsChanged(next.Packages));
    }

    public async Task Track(PackageId package)
    {
        package = PackageId.Create(package.Owner, package.Name);
        if (!Snapshot.Packages.Contains(package))
        {
            var next = Snapshot with { Packages = [.. Snapshot.Packages, package] };
            await Save(next, new AppsChanged(next.Packages));
        }
    }
}
