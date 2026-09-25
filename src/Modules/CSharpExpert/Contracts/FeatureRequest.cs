using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.feature-request")]
public sealed record FeatureRequest(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] string Description);
