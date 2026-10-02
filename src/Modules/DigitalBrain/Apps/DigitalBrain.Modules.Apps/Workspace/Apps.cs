using DigitalBrain.Core;
using DigitalBrain.Contracts;
using DigitalBrain.Apps.Signals;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GenerateSerializer]
internal sealed record InstalledAppsState
{
    [Id(0)] public PackageId[] Packages { get; init; } = [];
}

[GrainType("apps.installed")]
internal sealed class Apps(
    [PersistentState("installed-apps", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<InstalledAppsState> store)
    : Neuron<InstalledAppsState>(store), IApps
{
    public Task<PackageId[]> List() => Task.FromResult(Snapshot.Packages.ToArray());

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
