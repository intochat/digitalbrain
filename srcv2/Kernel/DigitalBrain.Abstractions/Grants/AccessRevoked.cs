namespace DigitalBrain.Abstractions.Grants;

[GenerateSerializer]
[Alias("db.access-revoked")]
public sealed record AccessRevoked(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] PrincipalId Grantee,
    [property: Id(2)] NeuronId Subject,
    [property: Id(3)] GrantKind Kind) : Synapse;

