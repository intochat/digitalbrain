using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Specs;

[ModuleConfiguration(typeof(SpecsConfigurationContract))]
public sealed class SpecsModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(SpecsModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton(TimeProvider.System);
    }
}

public sealed class SpecsModuleOptions;

public sealed class SpecsConfigurationContract() : ModuleConfigurationContract<SpecsModule, SpecsModuleOptions>
{
    protected override ModuleDefinition Compile(SpecsModuleOptions options) => SpecsModule.Define();
}
