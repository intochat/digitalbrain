using Orleans;

namespace DigitalBrain;

[GenerateSerializer]
[Alias("db.synapse")]
public abstract record Synapse
{
    [Id(0)]
    public SynapseMetadata? Metadata { get; init; }

    public SynapseMetadata Stamped => Metadata
        ?? throw new InvalidOperationException($"{GetType().Name} has not been stamped: metadata is assigned when a neuron fires a synapse.");
}
