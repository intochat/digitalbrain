namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.synapse-binding")]
public sealed record SynapseBinding(
    [property: Id(0)] Guid BindingId,
    [property: Id(1)] NeuronId Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] NeuronId Target,
    [property: Id(4)] string? Transform,
    [property: Id(5)] DateTimeOffset? ExpiresAt);
