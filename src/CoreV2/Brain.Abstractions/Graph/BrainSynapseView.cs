using Orleans.Concurrency;

namespace Brain.Abstractions.Graph;

[GenerateSerializer, Immutable]
public sealed record BrainSynapseView
{
    public BrainSynapseView(
        Guid id,
        long revision,
        string sourceNeuronId,
        string targetNeuronId,
        string inputContractId,
        string outputContractId,
        string status,
        long usageCount,
        Guid provenanceActivityId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A synapse identity is required.", nameof(id));
        }
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNeuronId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNeuronId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputContractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        if (usageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usageCount));
        }
        if (provenanceActivityId == Guid.Empty)
        {
            throw new ArgumentException("A provenance activity is required.", nameof(provenanceActivityId));
        }

        Id = id;
        Revision = revision;
        SourceNeuronId = sourceNeuronId;
        TargetNeuronId = targetNeuronId;
        InputContractId = inputContractId;
        OutputContractId = outputContractId;
        Status = status;
        UsageCount = usageCount;
        ProvenanceActivityId = provenanceActivityId;
    }

    [Id(0)] public Guid Id { get; }
    [Id(1)] public long Revision { get; }
    [Id(2)] public string SourceNeuronId { get; }
    [Id(3)] public string TargetNeuronId { get; }
    [Id(4)] public string InputContractId { get; }
    [Id(5)] public string OutputContractId { get; }
    [Id(6)] public string Status { get; }
    [Id(7)] public long UsageCount { get; }
    [Id(8)] public Guid ProvenanceActivityId { get; }
}
