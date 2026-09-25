using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.profile")]
public sealed record CodingProfile(
    [property: Id(0)] string PlannerAgentId,
    [property: Id(1)] string ImplementerAgentId,
    [property: Id(2)] int MaxFixAttempts,
    [property: Id(3)] bool BuildAndTestEachStep,
    [property: Id(4)] string ReviewRules,
    [property: Id(5)] string NuGetPolicy,
    [property: Id(6)] string? ReviewerAgentId = null)
{
    public static readonly CodingProfile Default = new(
        PlannerAgentId: "planner",
        ImplementerAgentId: "implementer",
        MaxFixAttempts: 3,
        BuildAndTestEachStep: true,
        ReviewRules: "no empty summary comments; self-explanatory naming; minimal inline comments",
        NuGetPolicy: "latest");
}
