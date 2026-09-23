using DigitalBrain.Core;

namespace DigitalBrain.Marketplace;

// Marketplace exposes no public option members; the contract exists so the module participates in
// manifest composition like every other product module.
public sealed class MarketplaceModuleOptions
{
}

public sealed class MarketplaceConfigurationContract() : ModuleConfigurationContract<MarketplaceModule, MarketplaceModuleOptions>
{
    protected override ModuleDefinition Compile(MarketplaceModuleOptions options) => MarketplaceModule.Define();
}
