namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.instance-retired")]
public sealed record InstanceRetired(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject) : Synapse;

