namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.open-workspace")]
public sealed record OpenWorkspace(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] long? ExpectedVersion = null);