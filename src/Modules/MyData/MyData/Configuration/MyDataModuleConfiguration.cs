using DigitalBrain.Core;

namespace DigitalBrain.MyData;

// My Data exposes no public configuration of its own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class MyDataModuleOptions
{
}

public sealed class MyDataConfigurationContract() : ModuleConfigurationContract<MyDataModule, MyDataModuleOptions>
{
    protected override ModuleDefinition Compile(MyDataModuleOptions options) => MyDataModule.Define();
}
