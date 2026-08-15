namespace DigitalBrain.Abstractions.Grants;

[GenerateSerializer]
[Alias("db.grant-access")]
public sealed record GrantAccess(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] PrincipalId Grantee,
    [property: Id(2)] NeuronId Subject,
    [property: Id(3)] GrantKind Kind,
    [property: Id(4)] string? Intent = null) : RequestSynapse<AccessGranted>;

