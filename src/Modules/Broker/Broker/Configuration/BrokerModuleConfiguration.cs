using DigitalBrain.Core;

namespace DigitalBrain.Broker;

// Broker exposes no public option members; the contract exists so the module participates in
// manifest composition like every other product module.
public sealed class BrokerModuleOptions
{
}

public sealed class BrokerConfigurationContract() : ModuleConfigurationContract<BrokerModule, BrokerModuleOptions>
{
    protected override ModuleDefinition Compile(BrokerModuleOptions options) => BrokerModule.Define();
}
