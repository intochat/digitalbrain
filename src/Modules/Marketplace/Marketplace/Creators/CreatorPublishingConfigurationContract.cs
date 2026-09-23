using DigitalBrain.Core;

namespace DigitalBrain.Marketplace.Creators;

public sealed class CreatorPublishingModuleOptions
{
}

public sealed class CreatorPublishingConfigurationContract()
    : ModuleConfigurationContract<CreatorPublishingModule, CreatorPublishingModuleOptions>
{
    protected override ModuleDefinition Compile(CreatorPublishingModuleOptions options) => CreatorPublishingModule.Define();
}
