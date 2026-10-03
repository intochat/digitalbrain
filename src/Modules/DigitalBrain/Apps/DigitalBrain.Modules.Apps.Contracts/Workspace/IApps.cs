using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Apps;

[Alias("apps.installed"), DefaultGrainType("apps.installed")]
public interface IApps : INeuron
{
    Task<PackageId[]> List();
    Task<InstalledAppsPage> Page(int offset, int limit);
    Task Track(PackageId package);
}

[GenerateSerializer, Alias("apps.installed-page")]
public sealed record InstalledAppsPage([property: Id(0)] PackageId[] Packages, [property: Id(1)] bool Ready = true);
