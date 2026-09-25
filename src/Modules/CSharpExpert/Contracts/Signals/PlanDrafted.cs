using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.plan-drafted")]
public sealed record PlanDrafted(
    [property: Id(0)] string RunId,
    [property: Id(1)] CodingPlan Plan) : Signal;
