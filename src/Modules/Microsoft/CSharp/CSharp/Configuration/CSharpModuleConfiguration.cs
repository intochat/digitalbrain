using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpConfigurationContract() : ModuleConfigurationContract<CSharpModule, CSharpOptions>("SourceRoot")
{
    protected override ModuleDefinition Compile(CSharpOptions options) => CSharpModule.Define(options);
}

public static class CSharpModuleConfiguration
{
    public static ModuleConfiguration<CSharpModule> WithSandbox(this ModuleConfiguration<CSharpModule> module, string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        module.ConfigureOptions<CSharpOptions>(o => o.SourceRoot = Path.GetFullPath(sourceRoot), "SourceRoot");
        return module;
    }
}
