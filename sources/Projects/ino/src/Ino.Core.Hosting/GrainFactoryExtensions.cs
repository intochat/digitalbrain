using Orleans;

namespace Ino.Core.Hosting;

public static class GrainFactoryExtensions
{
    public static IDiscovery GetDiscovery(this IGrainFactory grains) => grains.GetGrain<IDiscovery>(0);
}
