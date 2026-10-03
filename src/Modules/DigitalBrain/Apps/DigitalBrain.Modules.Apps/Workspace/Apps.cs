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
    [Id(2)] public int DiscoveryOffset { get; init; }
}

[GrainType("apps.installed")]
internal sealed class Apps(
    [PersistentState("installed-apps", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<InstalledAppsState> store,
    ModuleInventory modules)
    : Neuron<InstalledAppsState>(store), IApps
{
    public override DigitalBrain.Kernel.Enforcement.NeuronAccess Access(string operation) => new(this.GetPrimaryKeyString());

    public async Task<PackageId[]> List()
    {
        while (!Snapshot.Discovered) { await DiscoverBatch(); }
        return Snapshot.Packages.ToArray();
    }

    public async Task<InstalledAppsPage> Page(int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 21);
        if (!Snapshot.Discovered) { await DiscoverBatch(); }
        if (!Snapshot.Discovered) { return new([], Ready: false); }
        return new(Snapshot.Packages.OrderBy(id => id.ToString(), StringComparer.Ordinal).Skip(offset).Take(limit).ToArray());
    }

    private async Task DiscoverBatch()
    {
        // Backfill installations predating this index once. Normal reads never scan the
        // marketplace or activation registry; IApp tracks every new installation itself.
        var prefix = this.GetPrimaryKeyString() + "/packages/";
        var candidates = new HashSet<PackageId>();
        var complete = true;
        if (modules.Types.Any(module => module.FullName == "DigitalBrain.Registry.RegistryModule"))
        {
            var registry = GrainFactory.GetGrain<IRegistry>(IRegistry.Key);
            var instances = await registry.Instances("apps.app", skip: Snapshot.DiscoveryOffset, take: 1000);
            complete = instances.Count < 1000;
            foreach (var instance in instances.Where(instance => instance.Key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var parts = instance.Key[prefix.Length..].Split('/');
                if (parts.Length == 2) { candidates.Add(PackageId.Create(parts[0], parts[1])); }
            }
        }
        var tracked = Snapshot.Packages.ToHashSet();
        // Candidates are filtered by InstalledApps outside this grain, avoiding an Install/Track call cycle.
        tracked.UnionWith(candidates);
        var next = Snapshot with
        {
            Packages = tracked.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray(),
            Discovered = complete,
            DiscoveryOffset = Snapshot.DiscoveryOffset + 1000
        };
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
