using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GrainType("apps.package-directory")]
internal sealed class PackageDirectoryNeuron(
    [PersistentState("package-directory", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PackageDirectoryState> store,
    TimeProvider clock)
    : Neuron<PackageDirectoryState>(store), IPackageDirectory
{
    public Task<IReadOnlyList<PackageListing>> List()
        => Task.FromResult<IReadOnlyList<PackageListing>>(Snapshot.Listings.Values.OrderBy(listing => listing.Package.ToString(), StringComparer.Ordinal).ToArray());

    public async Task<PackageListing?> Refresh(PackageId package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var source = GrainFactory.GetGrain<IPackage>(package.ToString());
        var snapshot = await source.Read();
        if (snapshot.Published is not { } published) { return Snapshot.Listings.GetValueOrDefault(package.ToString()); }
        var current = Snapshot.Listings.GetValueOrDefault(package.ToString());
        if (current?.Revision == published) { return current; }
        var manifest = (await source.ReadRevision(published)).Content.Manifest;
        var listing = new PackageListing(package, manifest.Title, manifest.Description, published, snapshot.ForkedFrom, clock.GetUtcNow());
        var listings = new Dictionary<string, PackageListing>(Snapshot.Listings) { [package.ToString()] = listing };
        await Save(Snapshot with { Listings = listings }, new PackageListed(listing));
        return listing;
    }
}
