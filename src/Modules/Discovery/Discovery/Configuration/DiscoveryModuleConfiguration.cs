using DigitalBrain.Core;

namespace DigitalBrain.Discovery;

// Discovery exposes no public settings of its own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class DiscoveryModuleOptions
{
}

public sealed class DiscoveryConfigurationContract() : ModuleConfigurationContract<DiscoveryModule, DiscoveryModuleOptions>
{
    protected override ModuleDefinition Compile(DiscoveryModuleOptions options) => DiscoveryModule.Define();
}
