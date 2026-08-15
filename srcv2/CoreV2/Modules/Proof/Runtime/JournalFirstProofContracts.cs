using Brain.Abstractions.Runtime;
using Orleans.Concurrency;

namespace Brain.Modules.Proof;

public interface IProofSourceNeuron : IGrainWithStringKey
{
    Task<string> RunAsync(ProofNeuronRequest request);
}

public interface IProofAssessmentNeuron : IGrainWithStringKey
{
    Task<string> ReceiveAsync(ProofDelivery delivery);
}

[GenerateSerializer, Immutable]
public sealed record ProofNeuronRequest(
    [property: Id(0)] Guid ActivityId,
    [property: Id(1)] BrainOperationInvocation Invocation,
    [property: Id(2)] string Value);

[GenerateSerializer, Immutable]
public sealed record ProofDelivery(
    [property: Id(0)] Guid ActivityId,
    [property: Id(1)] BrainOperationInvocation Invocation,
    [property: Id(2)] Guid FiringId,
    [property: Id(3)] Guid SynapseId,
    [property: Id(4)] long SynapseRevision,
    [property: Id(5)] string Value);
