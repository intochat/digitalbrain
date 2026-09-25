using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-state")]
internal sealed record CodingRunState(
    [property: Id(0)] CodingRunStatus Status,
    [property: Id(1)] FeatureRequest? Request,
    [property: Id(2)] ProjectModel? Model,
    [property: Id(3)] CodingPlan? Plan,
    [property: Id(4)] string? FailureReason,
    [property: Id(5)] IReadOnlyList<string> Clarifications,
    [property: Id(6)] string? WorkspaceRoot,
    [property: Id(7)] string? WorkspaceSolutionPath,
    [property: Id(8)] int StepsCompleted,
    [property: Id(9)] int FixAttempts,
    [property: Id(10)] string? Diff,
    [property: Id(11)] BuildOutcome? Build,
    [property: Id(12)] TestOutcome? Test,
    [property: Id(13)] IReadOnlyList<DiagnosticGroup> Diagnostics,
    [property: Id(14)] IReadOnlyList<string> ReviewFindings,
    [property: Id(15)] IReadOnlyList<string> CompletedStepDiffs)
{
    public static readonly CodingRunState Empty = new(
        CodingRunStatus.Requested, null, null, null, null, [], null, null, 0, 0, null, null, null, [], [], []);
}
