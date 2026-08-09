using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Runtime;

internal sealed record CandidateInvocationResult(
    bool Committed,
    IReadOnlyList<SynapseEnvelope> Outputs);
