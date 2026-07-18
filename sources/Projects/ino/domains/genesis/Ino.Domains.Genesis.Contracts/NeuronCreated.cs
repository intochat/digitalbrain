using Ino.Core;

namespace Ino.Domains.Genesis.Contracts;

/// <summary>
/// Emitted by <c>CreatorNeuron</c> when a new dynamic neuron has been
/// compiled, validated, and registered via <see cref="INeuronRegistry"/>.
/// Closes the loop opened by <c>L1Proposal</c>: ino noticed a missed-intent
/// cluster, drafted a plan body, and now the next matching prompt will
/// route through <c>RoslynPlan</c>.
/// </summary>
[GenerateSerializer]
public sealed record NeuronCreated(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string NeuronId,
    [property: Id(2)] string UserId,
    [property: Id(3)] DateTimeOffset CreatedAt) : ISynapse;

/// <summary>
/// Emitted when <c>CreatorNeuron</c> tried to compile a draft body but the
/// Roslyn compilation failed. Lets the inspector surface why a proposal
/// didn't activate without crashing the kernel L1 loop.
/// </summary>
[GenerateSerializer]
public sealed record NeuronActivationFailed(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string NeuronId,
    [property: Id(2)] string UserId,
    [property: Id(3)] string Reason,
    [property: Id(4)] DateTimeOffset FailedAt) : ISynapse;
