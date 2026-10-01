using Orleans;

namespace DigitalBrain.Identity;

[GenerateSerializer, Alias("identity.account")]
public sealed record Account
{
    [Id(0)] public required string AccountId { get; init; }
    [Id(1)] public required string Name { get; init; }
    [Id(2)] public required string OwnerPrincipalId { get; init; }
    [Id(3)] public DateTimeOffset CreatedAt { get; init; }
}

[GenerateSerializer, Alias("identity.member")]
public sealed record Member
{
    [Id(0)] public required string PrincipalId { get; init; }
    [Id(1)] public required string AccountId { get; init; }
    [Id(2)] public required string BrainId { get; init; }
    [Id(3)] public required MemberRole Role { get; init; }
    [Id(4)] public required string DisplayName { get; init; }
    [Id(5)] public DateTimeOffset JoinedAt { get; init; }
}

[GenerateSerializer, Alias("identity.invitation")]
public sealed record Invitation
{
    [Id(0)] public required string Code { get; init; }
    [Id(1)] public required string AccountId { get; init; }
    [Id(2)] public required string WorkspaceId { get; init; }
    [Id(3)] public required MemberRole Role { get; init; }
    [Id(4)] public string? Email { get; init; }
    [Id(5)] public DateTimeOffset CreatedAt { get; init; }
    [Id(6)] public bool Accepted { get; init; }
}

// The workspace directory: accounts, their members, and pending invitations. Hosted as one
// neuron so a login can resolve a principal without knowing which account it belongs to yet.
public interface IIdentityDirectory : IGrainWithStringKey
{
    Task<Member> RegisterAsync(string principalId, string password, string displayName, CancellationToken cancellationToken = default);
    Task<Member?> AuthenticateAsync(string principalId, string password, CancellationToken cancellationToken = default);

    Task<Member?> FindMemberAsync(string principalId, CancellationToken cancellationToken = default);

    // A principal may reach a brain only when it holds a member record for it, whether it owns
    // the account or the brain was shared with it.
    Task<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default);

    Task<Member> ShareBrainAsync(string accountId, string brainId, string principalId, string displayName, MemberRole role, CancellationToken cancellationToken = default);
}

// Stable grain keys for the identity directory and per-workspace grant store.
public static class IdentityGrains
{
    public const string Directory = "intochat-identity-directory";

    public const string GrantsPrefix = "intochat-grants-";

    public static string Grants(string workspaceId) => GrantsPrefix + workspaceId;

    // The grant store is keyed by the prefixed grain key; grants themselves carry the bare
    // workspace id, which is what the call filter compares against the caller.
    public static string WorkspaceOfGrantStore(string grainKey) =>
        grainKey.StartsWith(GrantsPrefix, StringComparison.Ordinal)
            ? grainKey[GrantsPrefix.Length..]
            : grainKey;
}
