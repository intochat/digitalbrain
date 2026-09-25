using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.build-failed")]
public sealed record BuildFailed(
    [property: Id(0)] string RunId,
    [property: Id(1)] int StepNumber,
    [property: Id(2)] IReadOnlyList<DiagnosticGroup> Diagnostics) : Signal;
