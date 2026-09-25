using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class CSharpExpertModuleOptions
{
}

public sealed class CSharpExpertConfigurationContract() : ModuleConfigurationContract<CSharpExpertModule, CSharpExpertModuleOptions>
{
    protected override ModuleDefinition Compile(CSharpExpertModuleOptions options) => CSharpExpertModule.Define();
}
