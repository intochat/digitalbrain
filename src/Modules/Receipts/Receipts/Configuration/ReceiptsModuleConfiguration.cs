using DigitalBrain.Core;

namespace DigitalBrain.Receipts;

// Receipts expose no public configuration of their own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class ReceiptsModuleOptions
{
}

public sealed class ReceiptsConfigurationContract() : ModuleConfigurationContract<ReceiptsModule, ReceiptsModuleOptions>
{
    protected override ModuleDefinition Compile(ReceiptsModuleOptions options) => ReceiptsModule.Define();
}
