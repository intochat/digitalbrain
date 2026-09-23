using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Apps;

[ModuleConfiguration(typeof(AppsConfigurationContract))]
public sealed class AppsModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(AppsModule));

    public void Configure(ISiloBuilder silo) => ArgumentNullException.ThrowIfNull(silo);
}
