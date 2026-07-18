using Orleans;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record ProposalEntry(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string UserId,
    [property: Id(2)] string ClusterKey,
    [property: Id(3)] string ExamplePrompt,
    [property: Id(4)] string[] AllPrompts,
    [property: Id(5)] int Occurrences,
    [property: Id(6)] DateTimeOffset ProposedAt,
    [property: Id(7)] ProposalStatus Status,
    [property: Id(8)] string? ActivatedNeuronId,
    [property: Id(9)] DateTimeOffset? DecidedAt,
    [property: Id(10)] string? DecidedBy);
