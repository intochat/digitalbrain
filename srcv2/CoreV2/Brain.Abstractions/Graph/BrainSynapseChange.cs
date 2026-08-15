using Orleans.Concurrency;

namespace Brain.Abstractions.Graph;

[GenerateSerializer, Immutable]
public sealed record BrainSynapseChange
{
    public BrainSynapseChange(
        string workspaceId,
        BrainNeuronView source,
        BrainNeuronView target,
        string inputContractId,
        string outputContractId,
        Guid provenanceActivityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputContractId);
        if (provenanceActivityId == Guid.Empty)
        {
            throw new ArgumentException("A provenance activity is required.", nameof(provenanceActivityId));
        }

        WorkspaceId = workspaceId;
        Source = source;
        Target = target;
        InputContractId = inputContractId;
        OutputContractId = outputContractId;
        ProvenanceActivityId = provenanceActivityId;
    }

    [Id(0)] public string WorkspaceId { get; }
    [Id(1)] public BrainNeuronView Source { get; }
    [Id(2)] public BrainNeuronView Target { get; }
    [Id(3)] public string InputContractId { get; }
    [Id(4)] public string OutputContractId { get; }
    [Id(5)] public Guid ProvenanceActivityId { get; }
}
