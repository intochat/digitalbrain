using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Time;

[ModuleConfiguration(typeof(TimeConfigurationContract))]
public sealed class TimeModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(TimeModule));
    public void Configure(ISiloBuilder silo) => silo.AddTime();
}