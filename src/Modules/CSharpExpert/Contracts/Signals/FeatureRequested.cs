using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.feature-requested")]
public sealed record FeatureRequested(
    [property: Id(0)] string RunId,
    [property: Id(1)] FeatureRequest Request) : Signal;
