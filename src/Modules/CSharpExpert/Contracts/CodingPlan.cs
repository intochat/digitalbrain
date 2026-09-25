using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.plan")]
public sealed record CodingPlan(
    [property: Id(0)] string Summary,
    [property: Id(1)] IReadOnlyList<PlanStep> Steps,
    [property: Id(2)] IReadOnlyList<string> OpenQuestions);

[GenerateSerializer, Alias("csharp-expert.plan-step")]
public sealed record PlanStep(
    [property: Id(0)] int Number,
    [property: Id(1)] string Title,
    [property: Id(2)] IReadOnlyList<string> Files,
    [property: Id(3)] string Detail);
