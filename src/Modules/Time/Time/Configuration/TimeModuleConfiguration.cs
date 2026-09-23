using DigitalBrain.Core;

namespace DigitalBrain.Time;

// Time exposes no public configuration of its own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class TimeModuleOptions
{
}

public sealed class TimeConfigurationContract() : ModuleConfigurationContract<TimeModule, TimeModuleOptions>
{
    protected override ModuleDefinition Compile(TimeModuleOptions options) => TimeModule.Define();
}