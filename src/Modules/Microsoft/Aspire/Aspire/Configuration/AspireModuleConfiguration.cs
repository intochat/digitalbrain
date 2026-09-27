using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.Aspire;

public sealed class AspireConfigurationContract() : ModuleConfigurationContract<AspireModule, AspireOptions>("ApplicationName")
{
    protected override ModuleDefinition Compile(AspireOptions options) => AspireModule.Define(options);
}
