namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.instance-registered")]
public sealed record InstanceRegistered(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] RegisteredInstance Instance) : Synapse;

