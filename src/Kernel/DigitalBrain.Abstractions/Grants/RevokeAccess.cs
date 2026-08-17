namespace DigitalBrain.Abstractions.Grants;

[GenerateSerializer]
[Alias("db.revoke-access")]
public sealed record RevokeAccess(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] PrincipalId Grantee,
    [property: Id(2)] NeuronId Subject,
    [property: Id(3)] GrantKind Kind) : RequestSynapse<AccessRevoked>;

