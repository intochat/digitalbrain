using System.Collections.Immutable;
using Brain.Abstractions.Identity;

namespace Brain.Product.Abstractions.Authority;

public sealed record BrainAccessGrant
{
    private BrainAccessGrant(
        WorkspaceId workspace,
        PrincipalId principal,
        ImmutableArray<string> roles,
        ImmutableArray<string> grants,
        ImmutableArray<ConnectionReference> connections,
        int policyVersion,
        DateTimeOffset expiresAtUtc)
    {
        Workspace = workspace;
        Principal = principal;
        Roles = roles;
        Grants = grants;
        Connections = connections;
        PolicyVersion = policyVersion;
        ExpiresAtUtc = expiresAtUtc;
    }

    public WorkspaceId Workspace { get; }

    public PrincipalId Principal { get; }

    public ImmutableArray<string> Roles { get; }

    public ImmutableArray<string> Grants { get; }

    public ImmutableArray<ConnectionReference> Connections { get; }

    public int PolicyVersion { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public static BrainAccessGrant Create(
        WorkspaceId workspace,
        PrincipalId principal,
        IEnumerable<string> roles,
        IEnumerable<string> grants,
        IEnumerable<ConnectionReference> connections,
        int policyVersion,
        DateTimeOffset expiresAtUtc)
    {
        if (workspace.IsEmpty)
        {
            throw new ArgumentException("An access grant requires a workspace.", nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(principal.Value))
        {
            throw new ArgumentException("An access grant requires a principal.", nameof(principal));
        }

        if (policyVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion), "A policy version must be positive.");
        }

        var expiryUtc = expiresAtUtc.ToUniversalTime();
        if (expiryUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "An access grant must expire in the future.");
        }

        return new BrainAccessGrant(
            workspace,
            principal,
            CopyClaims(roles, nameof(roles)),
            CopyClaims(grants, nameof(grants)),
            CopyConnections(connections),
            policyVersion,
            expiryUtc);
    }

    private static ImmutableArray<string> CopyClaims(IEnumerable<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = values.ToImmutableArray();
        if (copy.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Access grant claims cannot be empty.", parameterName);
        }

        return copy;
    }

    private static ImmutableArray<ConnectionReference> CopyConnections(IEnumerable<ConnectionReference> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var copy = connections.ToImmutableArray();
        if (copy.Any(static connection => connection.IsEmpty))
        {
            throw new ArgumentException("Access grant connection references cannot be empty.", nameof(connections));
        }

        return copy;
    }
}
