namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.connected")]
public sealed record Connected(
    [property: Id(0)] Guid ConnectionId,
    [property: Id(1)] NeuronId Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] NeuronId Target) : Synapse;

