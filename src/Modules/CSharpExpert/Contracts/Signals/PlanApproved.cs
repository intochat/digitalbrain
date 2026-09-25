using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.plan-approved")]
public sealed record PlanApproved(
    [property: Id(0)] string RunId,
    [property: Id(1)] CodingPlan Plan) : Signal;
