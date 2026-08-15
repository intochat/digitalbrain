namespace DigitalBrain.Abstractions.Registry;

[GenerateSerializer]
[Alias("db.kind-installed")]
public sealed record KindInstalled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] KindRecord Kind) : Synapse;

