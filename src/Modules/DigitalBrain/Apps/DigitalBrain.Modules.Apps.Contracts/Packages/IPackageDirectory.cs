using DigitalBrain.Contracts;
using Orleans.Concurrency;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

// The marketplace index. Refresh re-reads the package, so a listing always reflects what its owner published.
[Alias("apps.package-directory"), DefaultGrainType("apps.package-directory")]
public interface IPackageDirectory : INeuron
{
    [AlwaysInterleave] Task<IReadOnlyList<PackageListing>> List();
    Task<PackageListing?> Refresh(PackageId package);
}
