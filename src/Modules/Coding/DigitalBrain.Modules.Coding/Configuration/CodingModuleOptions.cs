using DigitalBrain.Core;

namespace DigitalBrain.Coding;

public sealed class CodingModuleOptions : IModuleOptions
{
    // An omitted solution keeps the workspace closed until explicitly opened.
    public string? SolutionPath { get; set; }
    public string WorkspaceKey { get; set; } = "digitalbrain";

    // Bounds the Roslyn work of a check or a commit; a change set that overruns records the overrun as its detail.
    public TimeSpan EditDeadline { get; set; } = TimeSpan.FromSeconds(90);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceKey)) { throw new ArgumentException("Coding workspace key must not be empty."); }
        if (EditDeadline <= TimeSpan.Zero) { throw new ArgumentException("Coding edit deadline must be positive."); }
    }
}
