using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.workspace-prepared")]
public sealed record WorkspacePrepared(
    [property: Id(0)] string RunId,
    [property: Id(1)] string WorkspaceSolutionPath) : Signal;
