using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class CSharpExpertModuleOptions
{
    // Root for the isolated per-run workspaces. Defaults to a "wt-runs" folder beside the git repository,
    // or beside the solution's folder for a plain copy.
    public string? WorkspaceRoot { get; set; }

    public List<string> AllowedSolutionRoots { get; set; } = [];
}

public sealed class CSharpExpertConfigurationContract() : ModuleConfigurationContract<CSharpExpertModule, CSharpExpertModuleOptions>
{
    protected override ModuleDefinition Compile(CSharpExpertModuleOptions options) => CSharpExpertModule.Define(options);
}
