using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

[Alias("apps.installed"), DefaultGrainType("apps.installed")]
public interface IApps : INeuron
{
    Task<PackageId[]> List();
    Task Track(PackageId package);
}
