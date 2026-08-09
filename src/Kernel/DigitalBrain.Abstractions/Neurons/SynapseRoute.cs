namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.synapse-route")]
public sealed record SynapseRoute(
    [property: Id(0)] Guid BindingId,
    [property: Id(1)] NeuronId Target,
    [property: Id(2)] string? Transform);
