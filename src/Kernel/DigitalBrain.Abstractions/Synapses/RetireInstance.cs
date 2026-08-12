namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.retire-instance")]
public sealed record RetireInstance(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Subject) : RequestSynapse<InstanceRetired>;

