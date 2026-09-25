using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.DotNet;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-snapshot")]
public sealed record CodingRunSnapshot(
    [property: Id(0)] string RunId,
    [property: Id(1)] CodingRunStatus Status,
    [property: Id(2)] FeatureRequest? Request,
    [property: Id(3)] ProjectModel? Model,
    [property: Id(4)] CodingPlan? Plan,
    [property: Id(5)] string? FailureReason,
    [property: Id(6)] IReadOnlyList<string> Clarifications,
    [property: Id(7)] string? WorkspaceRoot,
    [property: Id(8)] string? WorkspaceSolutionPath,
    [property: Id(9)] int CurrentStep,
    [property: Id(10)] int TotalSteps,
    [property: Id(11)] int FixAttempts,
    [property: Id(12)] string? Diff,
    [property: Id(13)] BuildOutcome? Build,
    [property: Id(14)] TestOutcome? Test,
    [property: Id(15)] IReadOnlyList<DiagnosticGroup> Diagnostics);
