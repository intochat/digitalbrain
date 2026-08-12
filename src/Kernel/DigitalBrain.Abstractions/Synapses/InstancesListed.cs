namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.instances-listed")]
public sealed record InstancesListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] RegisteredInstance[] Items) : Synapse;

