using DigitalBrain.Core;

namespace DigitalBrain.Coding;

public sealed class CodingConfigurationContract() : ModuleConfigurationContract<CodingModule, CodingModuleOptions>(
    "SolutionPath", "WorkspaceKey", "TestProject", "EditDeadline")
{
    protected override ModuleDefinition Compile(CodingModuleOptions options) => CodingModule.Define(options);
}

public static class CodingModuleConfiguration
{
    public static ModuleConfiguration<CodingModule> WithSolution(this ModuleConfiguration<CodingModule> module, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        module.ConfigureOptions<CodingModuleOptions>(o => o.SolutionPath = Path.GetFullPath(path), "SolutionPath");
        return module;
    }
    public static ModuleConfiguration<CodingModule> WithOptions(this ModuleConfiguration<CodingModule> module, CodingModuleOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
}