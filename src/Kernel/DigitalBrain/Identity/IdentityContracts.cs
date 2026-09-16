namespace DigitalBrain.Identity;

/// <summary>A provider and immutable subject whose proof has already been verified by trusted server code.</summary>
[GenerateSerializer, Alias("db.v2.identity.VerifiedExternalIdentity")]
public sealed record VerifiedExternalIdentity([property: Id(0)] string Provider, [property: Id(1)] string Subject);

[Alias("db.v2.identity.WorkspaceRole")]
public enum WorkspaceRole { Member = 0, Owner = 1 }

[GenerateSerializer, Alias("db.v2.identity.IdentitySession")]
public sealed record IdentitySession(
    [property: Id(0)] string Token,
    [property: Id(1)] string CsrfToken,
    [property: Id(2)] string UserId,
    [property: Id(3)] bool IsOwner,
    [property: Id(4)] DateTimeOffset ExpiresAt);

[GenerateSerializer, Alias("db.v2.identity.AuthenticatedIdentity")]
public sealed record AuthenticatedIdentity(
    [property: Id(0)] string UserId,
    [property: Id(1)] bool IsOwner,
    [property: Id(2)] string CsrfToken,
    [property: Id(3)] Dictionary<string, WorkspaceRole> Memberships);

/// <summary>A safe account view: no session tokens, code hashes, or CSRF secrets.</summary>
[GenerateSerializer, Alias("db.v2.identity.IdentityAccount")]
public sealed record IdentityAccount(
    [property: Id(0)] string UserId,
    [property: Id(1)] bool Disabled,
    [property: Id(2)] Dictionary<string, WorkspaceRole> Memberships,
    [property: Id(3)] bool IsOwner);
