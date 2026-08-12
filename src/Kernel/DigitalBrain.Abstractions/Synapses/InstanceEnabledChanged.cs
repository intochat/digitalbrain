namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.instance-enabled-changed")]
public sealed record InstanceEnabledChanged(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] RegisteredInstance Instance) : Synapse;

