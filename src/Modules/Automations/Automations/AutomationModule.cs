using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Automations;

[ModuleConfiguration(typeof(AutomationsConfigurationContract))]
public sealed class AutomationsModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(AutomationsModule));

    public void Configure(ISiloBuilder silo) => silo.AddAutomations();
}
