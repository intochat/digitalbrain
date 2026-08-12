namespace DigitalBrain.Abstractions.Grants;

[GenerateSerializer]
[Alias("db.grants-listed")]
public sealed record GrantsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] GrantRecord[] Grants) : Synapse;

