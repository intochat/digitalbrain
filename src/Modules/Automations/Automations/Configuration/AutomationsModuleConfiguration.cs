using DigitalBrain.Core;

namespace DigitalBrain.Automations;

// Automations expose no public configuration of their own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class AutomationsModuleOptions
{
}

public sealed class AutomationsConfigurationContract() : ModuleConfigurationContract<AutomationsModule, AutomationsModuleOptions>
{
    protected override ModuleDefinition Compile(AutomationsModuleOptions options) => AutomationsModule.Define();
}
