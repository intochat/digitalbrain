namespace DigitalBrain.Capabilities.Identity.Model;

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain;

/// <summary>An active session record storing a hashed session token.</summary>
/// <param name="TokenHash">The SHA256 hex-encoded lower-case hash of the token.</param>
/// <param name="BrainId">The brain identifier.</param>
/// <param name="PrincipalId">The principal identifier.</param>
/// <param name="ExpiresAt">When the session expires.</param>
public sealed record Session(string TokenHash, string BrainId, string PrincipalId, DateTimeOffset ExpiresAt);

/// <summary>Pure domain model governing active sessions with hashed token lookup and auto-pruning.</summary>
public sealed record SessionBook
{
    /// <summary>All active sessions.</summary>
    public ImmutableArray<Session> Sessions { get; init; } = [];

    /// <summary>Computes the SHA256 lower-case hex hash of a session token.</summary>
    /// <param name="token">The plaintext session token.</param>
    /// <returns>The lowercase hex string hash.</returns>
    public static string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Starts a new session, auto-pruning expired sessions.</summary>
    /// <param name="tokenHash">The token hash.</param>
    /// <param name="brainId">The brain identifier.</param>
    /// <param name="principalId">The principal identifier.</param>
    /// <param name="expiresAt">When the session expires.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>The updated session book, the created session, and emitted domain facts.</returns>
    public (SessionBook Next, Session Session, ImmutableArray<Signal> Facts) Start(
        string tokenHash,
        string brainId,
        string principalId,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);

        var active = Sessions.Where(s => s.ExpiresAt > now).ToImmutableArray();
        var session = new Session(tokenHash, brainId, principalId, expiresAt);
        var next = this with { Sessions = active.Add(session) };
        var facts = ImmutableArray.Create<Signal>(new SessionStarted(tokenHash, brainId, principalId, expiresAt));
        return (next, session, facts);
    }

    /// <summary>Ends a session by its token hash.</summary>
    /// <param name="tokenHash">The token hash to remove.</param>
    /// <returns>The updated session book and emitted domain facts.</returns>
    public (SessionBook Next, ImmutableArray<Signal> Facts) End(string tokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        var remaining = Sessions.Where(s => s.TokenHash != tokenHash).ToImmutableArray();
        return (this with { Sessions = remaining }, []);
    }

    /// <summary>Finds an active unexpired session by token hash.</summary>
    /// <param name="tokenHash">The token hash.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>The session if found and not expired, otherwise null.</returns>
    public Session? Find(string tokenHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return null;
        }

        var candidate = Sessions.FirstOrDefault(s => s.TokenHash == tokenHash);
        return candidate is not null && candidate.ExpiresAt > now ? candidate : null;
    }
}
