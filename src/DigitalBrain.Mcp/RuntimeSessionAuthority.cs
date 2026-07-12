using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record IssuedRuntimeSession(RuntimeRequestContext Context, SessionPair Pair);

public sealed record ValidatedRuntimeSession(
    RuntimeRequestContext Context,
    DateTimeOffset AccessExpiresAt,
    long SessionVersion);

public sealed class RuntimeSessionAuthority(
    IClusterClient cluster,
    SessionTokenService tokens,
    TimeProvider timeProvider)
{
    private const string RefreshPrefix = "r1";
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    public async Task<IssuedRuntimeSession> CreateAsync(
        RuntimeRequestContext source,
        TimeSpan accessLifetime,
        string audience,
        CancellationToken cancellationToken = default)
    {
        ValidateAudience(audience);
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var sessionId = Guid.NewGuid().ToString("N");
        var refreshToken = CreateRefreshToken(sessionId);
        var refreshExpiresAt = now.Add(RefreshLifetime);
        var context = source with
        {
            SessionId = sessionId,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        var neuron = Session(sessionId);
        var initialized = await neuron.InitializeAsync(
            0,
            sessionId,
            audience,
            new SessionIdentity(context.TenantId, context.WorkspaceId, context.Principal),
            context.Assurance,
            context.Grants.Order(StringComparer.Ordinal).ToArray(),
            Hash(refreshToken),
            refreshExpiresAt).WaitAsync(cancellationToken).ConfigureAwait(false);
        return Issue(context, initialized, refreshToken, accessLifetime, now);
    }

    public async Task<IssuedRuntimeSession?> RefreshAsync(
        string refreshToken,
        TimeSpan accessLifetime,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        ValidateAudience(expectedAudience);
        if (!TryParseRefreshToken(refreshToken, out var sessionId)) return null;
        var neuron = Session(sessionId);
        var now = timeProvider.GetUtcNow();
        var current = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!MatchesSession(current, sessionId, expectedAudience)) return null;

        var replacement = CreateRefreshToken(sessionId);
        SessionRotation rotation;
        try
        {
            rotation = await neuron.RotateRefreshAsync(
                current.Revision,
                Hash(refreshToken),
                Hash(replacement),
                now.Add(RefreshLifetime),
                now).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeStateConflictException)
        {
            var latest = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (latest.RefreshReplay.Any(entry => FixedHashEquals(entry.ConsumedHash, Hash(refreshToken))))
                await RevokeAfterReplayAsync(neuron, latest, now, cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (rotation.Status == SessionRotationStatus.Replay)
        {
            await RevokeAfterReplayAsync(neuron, rotation.State, now, cancellationToken).ConfigureAwait(false);
            return null;
        }
        if (rotation.Status != SessionRotationStatus.Rotated) return null;

        var identity = rotation.State.Identity!;
        var context = new RuntimeRequestContext(
            identity.TenantId,
            identity.WorkspaceId,
            identity.Principal,
            sessionId,
            rotation.State.Assurance,
            Guid.NewGuid().ToString("N"),
            null,
            rotation.State.Grants.ToHashSet(StringComparer.Ordinal));
        return Issue(context, rotation.State, replacement, accessLifetime, now);
    }

    public async Task<bool> RevokeAsync(
        string refreshToken,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        ValidateAudience(expectedAudience);
        if (!TryParseRefreshToken(refreshToken, out var sessionId)) return false;
        var neuron = Session(sessionId);
        var current = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!MatchesSession(current, sessionId, expectedAudience)) return false;
        var presentedHash = Hash(refreshToken);
        if (!FixedHashEquals(current.RefreshTokenHash!, presentedHash) &&
            !current.RefreshReplay.Any(entry => FixedHashEquals(entry.ConsumedHash, presentedHash)))
            return false;
        if (current.RevokedAt is not null) return true;
        try
        {
            await neuron.RevokeAsync(current.Revision, timeProvider.GetUtcNow())
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RuntimeStateConflictException)
        {
            var latest = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return latest.RevokedAt is not null;
        }
    }

    public async Task<ValidatedRuntimeSession?> ValidateAccessAsync(
        string accessToken,
        string expectedAudience,
        CancellationToken cancellationToken = default)
    {
        ValidateAudience(expectedAudience);
        if (!tokens.TryValidate(
                accessToken,
                expectedAudience,
                out var context,
                out var accessExpiresAt,
                out var sessionVersion))
            return null;
        var state = await Session(context.SessionId).ReadAsync()
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        var expectedIdentity = new SessionIdentity(context.TenantId, context.WorkspaceId, context.Principal);
        if (!MatchesSession(state, context.SessionId, expectedAudience) ||
            state.Identity != expectedIdentity || state.Assurance != context.Assurance ||
            !state.Grants.SequenceEqual(context.Grants.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            !SessionTransitions.IsAccessValid(state, sessionVersion, timeProvider.GetUtcNow()))
            return null;
        return new(context, accessExpiresAt, sessionVersion);
    }

    private IssuedRuntimeSession Issue(
        RuntimeRequestContext context,
        SessionState state,
        string refreshToken,
        TimeSpan accessLifetime,
        DateTimeOffset now)
    {
        var accessExpiresAt = now.Add(accessLifetime);
        return new(
            context,
            new SessionPair(
                tokens.Issue(context, accessLifetime, state.Audience!, state.SessionVersion),
                refreshToken,
                state.RefreshExpiresAt,
                accessExpiresAt,
                state.Audience!));
    }

    private ISessionNeuron Session(string sessionId) =>
        cluster.GetGrain<ISessionNeuron>(RuntimeStateKeys.Session(sessionId));

    private static bool MatchesSession(SessionState state, string sessionId, string audience) =>
        state.OpaqueSessionId is not null &&
        string.Equals(state.OpaqueSessionId, sessionId, StringComparison.Ordinal) &&
        string.Equals(state.Audience, audience, StringComparison.Ordinal);

    internal static async Task RevokeAfterReplayAsync(
        ISessionNeuron neuron,
        SessionState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var current = state;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (current.RevokedAt is not null) return;
            try
            {
                await neuron.RevokeAsync(current.Revision, now)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (RuntimeStateConflictException)
            {
                current = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                if (current.RevokedAt is not null) return;
            }
        }
        throw new InvalidOperationException("Session replay revocation could not be committed.");
    }

    private static string CreateRefreshToken(string sessionId) =>
        $"{RefreshPrefix}.{sessionId}.{Base64Url(RandomNumberGenerator.GetBytes(32))}";

    private static bool TryParseRefreshToken(string? value, out string sessionId)
    {
        sessionId = string.Empty;
        if (value is not { Length: >= 70 and <= 128 }) return false;
        var parts = value.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], RefreshPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(parts[1], "N", out _) || parts[2].Length != 43 ||
            parts[2].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return false;
        sessionId = parts[1];
        return true;
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedHashEquals(string first, string second)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(first),
                Convert.FromHexString(second));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ValidateAudience(string audience)
    {
        if (audience is not (SessionAudiences.Mcp or SessionAudiences.Ui))
            throw new ArgumentException("A fixed runtime transport audience is required.", nameof(audience));
    }
}
