using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-finished")]
public sealed record RunFinished(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Diff) : Signal;
