using Orleans.Concurrency;

namespace Brain.Abstractions.Graph;

[GenerateSerializer, Immutable]
public sealed record BrainSnapshot
{
    public BrainSnapshot(
        string workspaceId,
        long sequence,
        DateTimeOffset observedAt,
        IReadOnlyList<BrainNeuronView> neurons,
        IReadOnlyList<BrainSynapseView> synapses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        ArgumentNullException.ThrowIfNull(neurons);
        ArgumentNullException.ThrowIfNull(synapses);
        WorkspaceId = workspaceId;
        Sequence = sequence;
        ObservedAt = observedAt;
        Neurons = neurons.ToArray();
        Synapses = synapses.ToArray();
    }

    [Id(0)] public string WorkspaceId { get; }
    [Id(1)] public long Sequence { get; }
    [Id(2)] public DateTimeOffset ObservedAt { get; }
    [Id(3)] public IReadOnlyList<BrainNeuronView> Neurons { get; }
    [Id(4)] public IReadOnlyList<BrainSynapseView> Synapses { get; }
}
