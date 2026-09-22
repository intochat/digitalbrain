namespace DigitalBrain.Microsoft.Roslyn;

[GenerateSerializer]
[Alias("coding.solution-map")]
public sealed record SolutionMap(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] IReadOnlyList<ProjectNode> Projects,
    [property: Id(2)] IReadOnlyList<ProjectEdge> References);