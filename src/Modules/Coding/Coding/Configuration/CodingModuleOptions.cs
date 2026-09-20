namespace DigitalBrain.Coding;

public sealed class CodingModuleOptions
{
    public const string SectionName = "DigitalBrain:Coding";

    // An omitted solution keeps the workspace closed until explicitly opened.
    public string? SolutionPath { get; set; }
    public string WorkspaceKey { get; set; } = "digitalbrain";
    public string? TestProject { get; set; }

    // Bounds the Roslyn work of a check or a commit; a change set that overruns records the overrun as its detail.
    public TimeSpan EditDeadline { get; set; } = TimeSpan.FromSeconds(90);
}