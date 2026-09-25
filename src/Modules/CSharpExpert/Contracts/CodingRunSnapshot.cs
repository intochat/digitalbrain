using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-snapshot")]
public sealed record CodingRunSnapshot(
    [property: Id(0)] string RunId,
    [property: Id(1)] CodingRunStatus Status,
    [property: Id(2)] FeatureRequest? Request,
    [property: Id(3)] ProjectModel? Model,
    [property: Id(4)] CodingPlan? Plan,
    [property: Id(5)] string? FailureReason);
