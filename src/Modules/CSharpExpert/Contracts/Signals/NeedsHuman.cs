using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.needs-human")]
public sealed record NeedsHuman(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Reason) : Signal;
