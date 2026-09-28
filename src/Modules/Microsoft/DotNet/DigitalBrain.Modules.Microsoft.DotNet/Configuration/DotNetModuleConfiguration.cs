using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.DotNet;

// DotNet exposes no public configuration of its own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class DotNetModuleOptions
{
}

public sealed class DotNetConfigurationContract() : ModuleConfigurationContract<DotNetModule, DotNetModuleOptions>
{
    protected override ModuleDefinition Compile(DotNetModuleOptions options) => new(typeof(DotNetModule));
}