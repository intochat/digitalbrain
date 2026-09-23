using DigitalBrain.Core;

namespace DigitalBrain.Compute;

// Compute exposes no public configuration of its own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class ComputeModuleOptions
{
}

public sealed class ComputeConfigurationContract() : ModuleConfigurationContract<ComputeModule, ComputeModuleOptions>
{
    protected override ModuleDefinition Compile(ComputeModuleOptions options) => ComputeModule.Define();
}
