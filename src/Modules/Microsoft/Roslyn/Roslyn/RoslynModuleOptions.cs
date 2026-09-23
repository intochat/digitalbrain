namespace DigitalBrain.Microsoft.Roslyn;

public sealed class RoslynModuleOptions
{
    // Same section Coding writes. The solution path stays Coding's setting; this grain only reads it.
    public const string SectionName = "DigitalBrain:Coding";

    public string? SolutionPath { get; set; }
    public string WorkspaceKey { get; set; } = "digitalbrain";
}