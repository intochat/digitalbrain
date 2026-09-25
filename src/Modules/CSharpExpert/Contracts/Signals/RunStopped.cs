using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-stopped")]
public sealed record RunStopped(
    [property: Id(0)] string RunId) : Signal;
