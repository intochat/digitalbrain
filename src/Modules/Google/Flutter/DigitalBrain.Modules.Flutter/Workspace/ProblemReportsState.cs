namespace DigitalBrain.Flutter.Workspace;

[GenerateSerializer, Alias("intochat.problem-reports-state")]
public sealed record ProblemReportsState
{
    [Id(0)] public List<ProblemReport> Reports { get; init; } = [];
}
