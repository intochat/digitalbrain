namespace DigitalBrain.Coding;

public sealed class CodingOptions
{
    public const string SectionName = "DigitalBrain:Coding";

    // An omitted solution keeps the workspace closed until explicitly opened.
    public string? SolutionPath { get; set; }
    public string WorkspaceKey { get; set; } = "digitalbrain";
    public string? TestProject { get; set; }
}
