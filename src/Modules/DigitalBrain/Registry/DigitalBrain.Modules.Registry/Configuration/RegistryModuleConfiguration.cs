using DigitalBrain.Core;

namespace DigitalBrain.Registry.Configuration;

// Registry exposes no public settings of its own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class RegistryModuleOptions
{
}

public sealed class RegistryConfigurationContract() : ModuleConfigurationContract<RegistryModule, RegistryModuleOptions>
{
    protected override ModuleDefinition Compile(RegistryModuleOptions options) => RegistryModule.Define();
}