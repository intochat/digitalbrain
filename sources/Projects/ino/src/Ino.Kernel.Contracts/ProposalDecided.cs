using Ino.Core;
using Orleans;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record ProposalDecided(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] ProposalStatus Decision,
    [property: Id(2)] string DecidedBy,
    [property: Id(3)] DateTimeOffset DecidedAt) : ISynapse;
