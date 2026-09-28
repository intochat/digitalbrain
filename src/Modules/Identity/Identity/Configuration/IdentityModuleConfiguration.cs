using DigitalBrain.Core;

namespace DigitalBrain.Identity;

// Identity exposes no public option members; the contract exists so the module participates in
// manifest composition like every other product module.
public sealed class IdentityModuleOptions
{
}

public sealed class IdentityConfigurationContract() : ModuleConfigurationContract<IdentityModule, IdentityModuleOptions>
{
    protected override ModuleDefinition Compile(IdentityModuleOptions options) => IdentityModule.Define();
}
