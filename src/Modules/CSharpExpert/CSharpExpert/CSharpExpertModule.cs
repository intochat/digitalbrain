using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.CSharpExpert;

[ModuleConfiguration(typeof(CSharpExpertConfigurationContract))]
public sealed class CSharpExpertModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(CSharpExpertModule));

    public void Configure(ISiloBuilder silo) { }
}
