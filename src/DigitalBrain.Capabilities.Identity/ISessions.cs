namespace DigitalBrain.Capabilities.Identity;

/// <summary>
/// identity@1, sessions: issue and end a session for a member another mechanism already authenticated (password, code,
/// external sign-in ticket). Same capability id as <see cref="IIdentity"/>. Every member is privileged: the runtime refuses it to
/// a module whose manifest does not grant it (<c>ModuleManifest.Privileged</c>, fail closed) and to callers that are not modules.
/// The cookie write stays with the host's HTTP edge (<c>ISessionIssuer</c> in DigitalBrain.Runtime.Http).
/// </summary>
[Capability("identity", 1)]
public interface ISessions
{
    /// <summary>Issues a session for <see cref="SessionRequest.Member"/>.</summary>
    /// <exception cref="ArgumentException">The request's claims name an identity-owned claim (<see cref="SessionClaims.IdentityOwned"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is zero or negative.</exception>
    /// <exception cref="UnauthorizedAccessException">The runtime caller has no grant for <c>Issue</c>.</exception>
    Task<SessionIssued> Issue(SessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ends the session; ending an unknown or ended session is a no-op.</summary>
    /// <exception cref="UnauthorizedAccessException">The runtime caller has no grant for <c>End</c>.</exception>
    Task End(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>The member of a live session; null when the session is unknown, ended or expired.</summary>
    /// <exception cref="UnauthorizedAccessException">The runtime caller has no grant for <c>Validate</c>.</exception>
    Task<Member?> Validate(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>A request to issue a session.</summary>
public sealed record SessionRequest
{
    /// <summary>The member the session is for; its principal, account, brain and role become the identity-owned claims.</summary>
    public required Member Member { get; init; }

    /// <summary>
    /// Extra claims, for example <c>sid</c>, <c>auth_time</c> and <c>amr</c>. A key naming an identity-owned claim
    /// (<see cref="SessionClaims.IdentityOwned"/>, case-insensitive) is rejected with <see cref="ArgumentException"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Claims { get; init; } = new Dictionary<string, string>();

    /// <summary>How long the session lives; null takes the implementation's default.</summary>
    public TimeSpan? Lifetime { get; init; }
}

/// <summary>An issued session.</summary>
/// <param name="SessionId">The session id; opaque.</param>
/// <param name="ExpiresAt">When the session expires.</param>
public sealed record SessionIssued(string SessionId, DateTimeOffset ExpiresAt);

/// <summary>The claim names identity owns: they always come from the member and never from <see cref="SessionRequest.Claims"/>.</summary>
public static class SessionClaims
{
    /// <summary>The principal id claim.</summary>
    public const string Principal = "principal";

    /// <summary>The account id claim.</summary>
    public const string Account = "account";

    /// <summary>The brain id claim.</summary>
    public const string Brain = "brain";

    /// <summary>The member role claim.</summary>
    public const string Role = "role";

    /// <summary>Every identity-owned claim name.</summary>
    public static IReadOnlyList<string> IdentityOwned { get; } = [Principal, Account, Brain, Role];

    /// <summary>True when <paramref name="name"/> is an identity-owned claim (case-insensitive, surrounding spaces ignored).</summary>
    public static bool IsIdentityOwned(string? name)
        => name is not null && IdentityOwned.Any(owned => string.Equals(owned, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks a request before it is issued: named claims, none identity-owned, a positive lifetime.</summary>
    /// <exception cref="ArgumentException">A claim key is identity-owned or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime is zero or negative.</exception>
    public static void Validate(SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Member);
        foreach (var key in request.Claims?.Keys ?? [])
        {
            if (string.IsNullOrWhiteSpace(key)) { throw new ArgumentException("A session claim needs a name.", nameof(request)); }
            if (IsIdentityOwned(key)) { throw new ArgumentException($"The claim '{key}' is owned by identity and cannot be set on a session.", nameof(request)); }
        }

        if (request.Lifetime is { } lifetime && lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), lifetime, "A session lifetime is positive.");
        }
    }

    /// <summary>The identity-owned claims of <paramref name="member"/>.</summary>
    public static IReadOnlyDictionary<string, string> Of(Member member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Principal] = member.PrincipalId,
            [Account] = member.AccountId,
            [Brain] = member.BrainId,
            [Role] = member.Role.ToString(),
        };
    }
}
