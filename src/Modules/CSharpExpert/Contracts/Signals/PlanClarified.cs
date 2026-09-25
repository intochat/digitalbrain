using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.plan-clarified")]
public sealed record PlanClarified(
    [property: Id(0)] string RunId,
    [property: Id(1)] string Clarification) : Signal;
