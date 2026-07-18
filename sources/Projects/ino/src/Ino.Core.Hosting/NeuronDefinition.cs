using Ino.Core;

namespace Ino.Core.Hosting;

[GenerateSerializer]
public sealed record NeuronDefinition(
    [property: Id(0)] NeuronId Id,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] Type CanonicalSynapseType,
    [property: Id(4)] string[] PromptExamples) : INeuronDefinition
{
    [Id(5)] public Type? PlanType { get; init; }
}
