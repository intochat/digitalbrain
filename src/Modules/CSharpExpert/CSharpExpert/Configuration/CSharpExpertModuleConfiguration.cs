using DigitalBrain.Core;

namespace DigitalBrain.CSharpExpert;

public sealed class CSharpExpertModuleOptions
{
    // Root for the isolated per-run workspace. Defaults to a sibling "wt-runs" of a git repository
    // (or the temp folder for a plain folder copy) when omitted.
    public string? WorkspaceRoot { get; set; }
}

public sealed class CSharpExpertConfigurationContract() : ModuleConfigurationContract<CSharpExpertModule, CSharpExpertModuleOptions>
{
    protected override ModuleDefinition Compile(CSharpExpertModuleOptions options) => CSharpExpertModule.Define(options);
}
