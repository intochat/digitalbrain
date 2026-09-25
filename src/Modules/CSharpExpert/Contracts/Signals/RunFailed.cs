using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-failed")]
public sealed record RunFailed(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Reason) : Signal;
