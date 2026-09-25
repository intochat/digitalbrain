namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-state")]
internal sealed record CodingRunState(
    [property: Id(0)] CodingRunStatus Status,
    [property: Id(1)] FeatureRequest? Request,
    [property: Id(2)] ProjectModel? Model,
    [property: Id(3)] CodingPlan? Plan,
    [property: Id(4)] string? FailureReason,
    [property: Id(5)] IReadOnlyList<string> Clarifications)
{
    public static readonly CodingRunState Empty = new(CodingRunStatus.Requested, null, null, null, null, []);
}
