namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.workspace-snapshot")]
public sealed record WorkspaceSnapshot(
    [property: Id(0)] string? SolutionPath,
    [property: Id(1)] WorkspacePhase Phase,
    [property: Id(2)] int ProjectCount,
    [property: Id(3)] int DocumentCount,
    [property: Id(4)] string? Detail,
    [property: Id(5)] long Generation,
    [property: Id(6)] bool ReloadNeeded = false);
