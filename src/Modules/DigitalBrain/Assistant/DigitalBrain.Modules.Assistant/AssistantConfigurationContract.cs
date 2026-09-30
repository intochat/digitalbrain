using DigitalBrain.Core;

namespace DigitalBrain.Assistant;

public sealed class AssistantModuleOptions
{
}

public sealed class AssistantConfigurationContract() : ModuleConfigurationContract<AssistantModule, AssistantModuleOptions>
{
    protected override ModuleDefinition Compile(AssistantModuleOptions options) => new(typeof(AssistantModule));
}
