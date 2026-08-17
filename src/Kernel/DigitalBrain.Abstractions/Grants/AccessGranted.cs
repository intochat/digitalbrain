namespace DigitalBrain.Abstractions.Grants;

[GenerateSerializer]
[Alias("db.access-granted")]
public sealed record AccessGranted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] GrantRecord Grant) : Synapse;

