namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.kinds-listed")]
public sealed record KindsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] KindRecord[] Kinds) : Synapse;

