using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public enum SessionRotationStatus { Rotated, Replay, Expired, Revoked, Rejected }

[GenerateSerializer, Alias("digitalbrain.runtime.session-identity")]
public sealed record SessionIdentity(
    [property: Id(0)] BrainOwnerId OwnerId,
    [property: Id(1)] ActorId ActorId);

[GenerateSerializer, Alias("digitalbrain.runtime.session-refresh-replay")]
public sealed record SessionRefreshReplay(
    [property: Id(0)] string ConsumedHash,
    [property: Id(1)] string ReplacementHash,
    [property: Id(2)] DateTimeOffset ConsumedAt);

[GenerateSerializer, Alias("digitalbrain.runtime.session-state")]
public sealed record SessionState(
    [property: Id(0)] int SchemaVersion,
    [property: Id(1)] long Revision,
    [property: Id(2)] string? OpaqueSessionId,
    [property: Id(3)] string? Audience,
    [property: Id(4)] SessionIdentity? Identity,
    [property: Id(5)] AuthAssurance Assurance,
    [property: Id(6)] string[] Grants,
    [property: Id(7)] long SessionVersion,
    [property: Id(8)] string? RefreshTokenHash,
    [property: Id(9)] DateTimeOffset RefreshExpiresAt,
    [property: Id(10)] DateTimeOffset? RevokedAt,
    [property: Id(11)] SessionRefreshReplay[] RefreshReplay)
{
    public static SessionState Empty() => new(
        RuntimeStateSchemas.Session,
        0,
        null,
        null,
        null,
        AuthAssurance.None,
        [],
        0,
        null,
        default,
        null,
        []);
}

[GenerateSerializer, Alias("digitalbrain.runtime.session-rotation")]
public sealed record SessionRotation(
    [property: Id(0)] SessionState State,
    [property: Id(1)] SessionRotationStatus Status);

[Alias("digitalbrain.runtime.i-session-neuron")]
public interface ISessionNeuron : IGrainWithStringKey
{
    [Alias("digitalbrain.runtime.session.read")]
    Task<SessionState> ReadAsync();
    [Alias("digitalbrain.runtime.session.initialize")]
    Task<SessionState> InitializeAsync(
        long expectedRevision,
        string opaqueSessionId,
        string audience,
        SessionIdentity identity,
        AuthAssurance assurance,
        string[] grants,
        string refreshTokenHash,
        DateTimeOffset refreshExpiresAt);
    [Alias("digitalbrain.runtime.session.rotate-refresh")]
    Task<SessionRotation> RotateRefreshAsync(
        long expectedRevision,
        string presentedRefreshHash,
        string replacementRefreshHash,
        DateTimeOffset replacementExpiresAt,
        DateTimeOffset now);
    [Alias("digitalbrain.runtime.session.revoke")]
    Task<SessionState> RevokeAsync(long expectedRevision, DateTimeOffset revokedAt);
    [Alias("digitalbrain.runtime.session.is-access-valid")]
    Task<bool> IsAccessValidAsync(long sessionVersion, DateTimeOffset now);
}

public static class SessionTransitions
{
    public const int MaximumRefreshReplayEntries = 64;

    public static SessionState Initialize(
        SessionState state,
        long expectedRevision,
        string opaqueSessionId,
        string audience,
        SessionIdentity identity,
        AuthAssurance assurance,
        string[] grants,
        string refreshTokenHash,
        DateTimeOffset refreshExpiresAt)
    {
        DemandRevision(state, expectedRevision);
        DemandId(opaqueSessionId, nameof(opaqueSessionId));
        DemandId(audience, nameof(audience));
        ValidateIdentity(identity);
        var canonicalGrants = ValidateAndCanonicalizeGrants(grants);
        if (!Enum.IsDefined(assurance) || assurance == AuthAssurance.None)
            throw new ArgumentException("An authenticated session assurance is required.", nameof(assurance));
        DemandHash(refreshTokenHash, nameof(refreshTokenHash));
        if (refreshExpiresAt == default) throw new ArgumentException("A refresh expiry is required.", nameof(refreshExpiresAt));
        if (state.OpaqueSessionId is not null)
        {
            if (string.Equals(state.OpaqueSessionId, opaqueSessionId, StringComparison.Ordinal) &&
                string.Equals(state.Audience, audience, StringComparison.Ordinal) && state.Identity == identity &&
                state.Assurance == assurance && state.Grants.SequenceEqual(canonicalGrants, StringComparer.Ordinal) &&
                string.Equals(state.RefreshTokenHash, refreshTokenHash, StringComparison.OrdinalIgnoreCase) &&
                state.RefreshExpiresAt == refreshExpiresAt) return state;
            throw new InvalidOperationException("A session grain cannot be rebound to another session.");
        }
        var next = state with
        {
            Revision = checked(state.Revision + 1),
            OpaqueSessionId = opaqueSessionId,
            Audience = audience,
            Identity = identity,
            Assurance = assurance,
            Grants = canonicalGrants,
            SessionVersion = 1,
            RefreshTokenHash = refreshTokenHash.ToLowerInvariant(),
            RefreshExpiresAt = refreshExpiresAt
        };
        Validate(next);
        return next;
    }

    public static SessionRotation RotateRefresh(
        SessionState state,
        long expectedRevision,
        string presentedRefreshHash,
        string replacementRefreshHash,
        DateTimeOffset replacementExpiresAt,
        DateTimeOffset now)
    {
        DemandMutable(state, expectedRevision);
        DemandHash(presentedRefreshHash, nameof(presentedRefreshHash));
        DemandHash(replacementRefreshHash, nameof(replacementRefreshHash));
        if (replacementExpiresAt <= now) throw new ArgumentException("Replacement refresh expiry must be in the future.", nameof(replacementExpiresAt));
        if (state.RevokedAt is not null) return new(state, SessionRotationStatus.Revoked);
        if (state.RefreshExpiresAt <= now) return new(state, SessionRotationStatus.Expired);
        if (state.RefreshReplay.Any(replay => string.Equals(replay.ConsumedHash, presentedRefreshHash, StringComparison.OrdinalIgnoreCase)))
            return new(state, SessionRotationStatus.Replay);
        if (!string.Equals(state.RefreshTokenHash, presentedRefreshHash, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(presentedRefreshHash, replacementRefreshHash, StringComparison.OrdinalIgnoreCase) ||
            state.RefreshReplay.Any(replay => string.Equals(replay.ConsumedHash, replacementRefreshHash, StringComparison.OrdinalIgnoreCase)))
            return new(state, SessionRotationStatus.Rejected);
        var replayEntry = new SessionRefreshReplay(
            presentedRefreshHash.ToLowerInvariant(),
            replacementRefreshHash.ToLowerInvariant(),
            now);
        var next = state with
        {
            Revision = checked(state.Revision + 1),
            SessionVersion = checked(state.SessionVersion + 1),
            RefreshTokenHash = replacementRefreshHash.ToLowerInvariant(),
            RefreshExpiresAt = replacementExpiresAt,
            RefreshReplay = state.RefreshReplay.Append(replayEntry)
                .OrderBy(entry => entry.ConsumedAt)
                .TakeLast(MaximumRefreshReplayEntries)
                .ToArray()
        };
        Validate(next);
        return new(next, SessionRotationStatus.Rotated);
    }

    public static SessionState Revoke(SessionState state, long expectedRevision, DateTimeOffset revokedAt)
    {
        DemandMutable(state, expectedRevision);
        if (state.RevokedAt is not null) return state;
        var next = state with
        {
            Revision = checked(state.Revision + 1),
            SessionVersion = checked(state.SessionVersion + 1),
            RevokedAt = revokedAt
        };
        Validate(next);
        return next;
    }

    public static bool IsAccessValid(SessionState state, long sessionVersion, DateTimeOffset now)
    {
        Validate(state);
        return state.OpaqueSessionId is not null && state.RevokedAt is null && state.RefreshExpiresAt > now &&
               sessionVersion > 0 && sessionVersion == state.SessionVersion;
    }

    public static void Validate(SessionState state)
    {
        if (state.SchemaVersion != RuntimeStateSchemas.Session || state.Revision < 0 || state.SessionVersion < 0 ||
            state.Grants is null || state.RefreshReplay is null ||
            state.RefreshReplay.Length > MaximumRefreshReplayEntries)
            throw new RuntimeStateIntegrityException("invalid session schema");
        var initialized = state.Revision > 0;
        if (initialized != (state.OpaqueSessionId is not null && state.Audience is not null && state.Identity is not null &&
                            state.RefreshTokenHash is not null && state.RefreshExpiresAt != default && state.SessionVersion > 0))
            throw new RuntimeStateIntegrityException("invalid session identity lifecycle");
        if (!initialized) return;
        DemandId(state.OpaqueSessionId!, nameof(state.OpaqueSessionId));
        DemandId(state.Audience!, nameof(state.Audience));
        ValidateIdentity(state.Identity!);
        if (!Enum.IsDefined(state.Assurance) || state.Assurance == AuthAssurance.None)
            throw new RuntimeStateIntegrityException("invalid session assurance");
        var canonicalGrants = ValidateAndCanonicalizeGrants(state.Grants);
        if (!state.Grants.SequenceEqual(canonicalGrants, StringComparer.Ordinal))
            throw new RuntimeStateIntegrityException("session grants are not canonical");
        DemandHash(state.RefreshTokenHash!, nameof(state.RefreshTokenHash));
        foreach (var replay in state.RefreshReplay)
        {
            DemandHash(replay.ConsumedHash, nameof(replay.ConsumedHash));
            DemandHash(replay.ReplacementHash, nameof(replay.ReplacementHash));
        }
    }

    private static void DemandMutable(SessionState state, long expectedRevision)
    {
        DemandRevision(state, expectedRevision);
        if (state.OpaqueSessionId is null) throw new InvalidOperationException("Session state is not initialized.");
    }

    private static void DemandRevision(SessionState state, long expectedRevision)
    {
        if (state.Revision != expectedRevision) throw new RuntimeStateConflictException(expectedRevision, state.Revision);
    }

    private static void ValidateIdentity(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.OwnerId.Value) || string.IsNullOrWhiteSpace(identity.ActorId.Value))
            throw new ArgumentException("A complete session identity is required.", nameof(identity));
    }

    private static string[] ValidateAndCanonicalizeGrants(IEnumerable<string> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var canonical = grants.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray();
        if (canonical.Length > 64 || canonical.Any(static grant =>
                string.IsNullOrWhiteSpace(grant) || grant.Length > 128 || grant.Any(char.IsControl)))
            throw new ArgumentException("Session grants must be unique, bounded capability names.", nameof(grants));
        return canonical;
    }

    private static void DemandId(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Session identifiers must be present and bounded.", name);
    }

    private static void DemandHash(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 digest is required.", name);
    }
}
