using DigitalBrain.Core;

namespace DigitalBrain.Apps;

public sealed class AppsConfigurationContract() : ModuleConfigurationContract<AppsModule, AppsModuleOptions>
{
    protected override ModuleDefinition Compile(AppsModuleOptions options) => AppsModule.Define();
}
