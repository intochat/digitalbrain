using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.project-model")]
public sealed record ProjectModel(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] IReadOnlyList<ProjectInfo> Projects,
    [property: Id(2)] IReadOnlyList<string> TargetFrameworks,
    [property: Id(3)] int ErrorCount,
    [property: Id(4)] int WarningCount,
    [property: Id(5)] string MapText);

[GenerateSerializer, Alias("csharp-expert.project-info")]
public sealed record ProjectInfo(
    [property: Id(0)] string Name,
    [property: Id(1)] string Path,
    [property: Id(2)] int DocumentCount);
