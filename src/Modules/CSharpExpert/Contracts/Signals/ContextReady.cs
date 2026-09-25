using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.context-ready")]
public sealed record ContextReady(
    [property: Id(0)] string RunId,
    [property: Id(1)] ProjectModel Model) : Signal;
