namespace DigitalBrain.Abstractions;

public enum GrantKind
{
    Read = 0,
    Watch = 1,
}

[GenerateSerializer]
[Alias("db.grant-access")]
public sealed record GrantAccess(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] PrincipalId Grantee,
    [property: Id(2)] NeuronId Subject,
    [property: Id(3)] GrantKind Kind,
    [property: Id(4)] string? Intent = null) : RequestSynapse<AccessGranted>;

[GenerateSerializer]
[Alias("db.access-granted")]
public sealed record AccessGranted(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] GrantRecord Grant) : Synapse;

[GenerateSerializer]
[Alias("db.revoke-access")]
public sealed record RevokeAccess(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] PrincipalId Grantee,
    [property: Id(2)] NeuronId Subject,
    [property: Id(3)] GrantKind Kind) : RequestSynapse<AccessRevoked>;

[GenerateSerializer]
[Alias("db.access-revoked")]
public sealed record AccessRevoked(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] PrincipalId Grantee,
    [property: Id(2)] NeuronId Subject,
    [property: Id(3)] GrantKind Kind) : Synapse;

[GenerateSerializer]
[Alias("db.list-grants")]
public sealed record ListGrants(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<GrantsListed>;

[GenerateSerializer]
[Alias("db.grants-listed")]
public sealed record GrantsListed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] GrantRecord[] Grants) : Synapse;

[GenerateSerializer]
[Alias("db.grant-record")]
public sealed record GrantRecord(
    [property: Id(0)] PrincipalId Grantee,
    [property: Id(1)] NeuronId Subject,
    [property: Id(2)] GrantKind Kind,
    [property: Id(3)] PrincipalId Grantor,
    [property: Id(4)] DateTimeOffset At,
    [property: Id(5)] string? Intent);
