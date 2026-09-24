using DigitalBrain.Core;

namespace DigitalBrain.Apps;

// Apps expose no public configuration of their own; the contract exists so the module participates
// in manifest composition like every other product module.
public sealed class AppsModuleOptions
{
}

public sealed class AppsConfigurationContract() : ModuleConfigurationContract<AppsModule, AppsModuleOptions>
{
    protected override ModuleDefinition Compile(AppsModuleOptions options) => AppsModule.Define();
}

public static class AppsModuleConfiguration
{
    public static ModuleConfiguration<AppsModule> AddApp<TApp>(this ModuleConfiguration<AppsModule> module)
        where TApp : Neuron, IAppDefinition
    {
        ArgumentNullException.ThrowIfNull(module);
        module.RegisterApp<TApp>();
        return module;
    }
}
