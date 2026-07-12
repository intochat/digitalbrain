using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orleans;

namespace DigitalBrain.Core.Runtime;

public enum PrincipalKind { User, Service, Operator }
public enum AuthAssurance { None, Password, Oidc, MutualTls, OperatorBootstrap }

[GenerateSerializer, Alias("digitalbrain.v2.tenant-id")]
public readonly record struct TenantId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v2.workspace-id")]
public readonly record struct WorkspaceId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v2.principal-ref")]
public readonly record struct PrincipalRef([property: Id(0)] string Value, [property: Id(1)] PrincipalKind Kind);

[GenerateSerializer, Alias("digitalbrain.v2.request-context")]
public sealed record RequestContext(
    [property: Id(0)] TenantId TenantId,
    [property: Id(1)] WorkspaceId WorkspaceId,
    [property: Id(2)] PrincipalRef Principal,
    [property: Id(3)] string SessionId,
    [property: Id(4)] AuthAssurance Assurance,
    [property: Id(5)] string CorrelationId,
    [property: Id(6)] string? IdempotencyKey,
    [property: Id(7)] IReadOnlySet<string> Grants);

public static class SessionAudiences
{
    public const string Mcp = "digitalbrain-v2";
    public const string Ui = "digitalbrain-v2-ui";

    public static string RequireFixedMcp(string? configuredAudience)
    {
        if (configuredAudience is null) return Mcp;
        if (!string.Equals(configuredAudience, Mcp, StringComparison.Ordinal))
            throw new InvalidOperationException("The MCP transport audience is fixed and cannot be empty, aliased, or shared with the UI transport.");
        return Mcp;
    }
}

public static class RequestScope
{
    public static string Id(RequestContext context)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tenant = context.TenantId.Value,
            workspace = context.WorkspaceId.Value,
            principalKind = (int)context.Principal.Kind,
            principal = context.Principal.Value
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }
}

[GenerateSerializer, Alias("digitalbrain.v2.persisted-actor-snapshot")]
public sealed record PersistedActorSnapshot(
    [property: Id(0)] TenantId TenantId,
    [property: Id(1)] WorkspaceId WorkspaceId,
    [property: Id(2)] PrincipalRef Principal,
    [property: Id(3)] AuthAssurance Assurance,
    [property: Id(4)] DateTimeOffset CapturedAt);

public static class GrainIds
{
    public static string Aggregate(TenantId tenant, WorkspaceId workspace, string aggregate) =>
        ScopePrefix(tenant, workspace) + "aggregate/" + Segment(aggregate);
    public static string Conversation(TenantId tenant, WorkspaceId workspace, string conversation) =>
        ScopePrefix(tenant, workspace) + "conversation/" + Segment(conversation);
    public static string Workflow(TenantId tenant, WorkspaceId workspace, string workflow) =>
        ScopePrefix(tenant, workspace) + "workflow/" + Segment(workflow);

    public static string ScopePrefix(TenantId tenant, WorkspaceId workspace) =>
        $"v2/{Segment(tenant.Value)}/{Segment(workspace.Value)}/";

    public static bool IsInScope(string? grainId, TenantId tenant, WorkspaceId workspace) =>
        !string.IsNullOrWhiteSpace(grainId) && grainId.StartsWith(ScopePrefix(tenant, workspace), StringComparison.Ordinal);

    private static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty grain id component is required.", nameof(value));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed class SessionTokenService
{
    private const string StructuredPrefix = "v2s";
    private readonly byte[] _key;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new(StringComparer.Ordinal);
    public SessionTokenService(byte[] key)
    {
        if (key.Length < 32) throw new ArgumentException("The session signing key must be at least 256 bits.", nameof(key));
        _key = key.ToArray();
    }
    public string Issue(RequestContext context, TimeSpan lifetime, string audience = SessionAudiences.Mcp)
    {
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (string.IsNullOrWhiteSpace(audience)) throw new ArgumentException("A non-empty session audience is required.", nameof(audience));
        if (string.IsNullOrWhiteSpace(context.SessionId) || string.IsNullOrWhiteSpace(context.TenantId.Value) ||
            string.IsNullOrWhiteSpace(context.WorkspaceId.Value) || string.IsNullOrWhiteSpace(context.Principal.Value))
            throw new ArgumentException("A complete request context is required.", nameof(context));
        if (context.SessionId.Length > 256 || context.TenantId.Value.Length > 256 || context.WorkspaceId.Value.Length > 256 ||
            context.Principal.Value.Length > 256 || audience.Length > 128 || context.Grants.Count > 64 ||
            context.Grants.Any(static grant => string.IsNullOrWhiteSpace(grant) || grant.Length > 128))
            throw new ArgumentException("Session claims exceed the signed transport bound.", nameof(context));

        var now = DateTimeOffset.UtcNow;
        var claims = new SessionClaims(
            2,
            context.SessionId,
            context.TenantId.Value,
            context.WorkspaceId.Value,
            context.Principal.Value,
            context.Principal.Kind,
            context.Assurance,
            audience,
            context.Grants.Order(StringComparer.Ordinal).ToArray(),
            now.ToUnixTimeSeconds(),
            now.Add(lifetime).ToUnixTimeSeconds());
        var encoded = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var body = StructuredPrefix + "." + encoded;
        var signature = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body)));
        return body + "." + signature;
    }

    public bool TryValidate(string token, out RequestContext context)
        => TryValidateCore(token, expectedAudience: null, out context, out _);

    public bool TryValidate(string token, string expectedAudience, out RequestContext context)
    {
        context = default!;
        return !string.IsNullOrWhiteSpace(expectedAudience) && TryValidateCore(token, expectedAudience, out context, out _);
    }

    public bool TryValidate(string token, string expectedAudience, out RequestContext context, out DateTimeOffset expiresAt)
    {
        context = default!;
        expiresAt = default;
        return !string.IsNullOrWhiteSpace(expectedAudience) && TryValidateCore(token, expectedAudience, out context, out expiresAt);
    }

    private bool TryValidateCore(string token, string? expectedAudience, out RequestContext context, out DateTimeOffset expiresAt)
    {
        context = default!;
        expiresAt = default;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384) return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != StructuredPrefix) return false;
        var body = string.Join('.', parts.Take(2));
        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body));
        byte[] actual;
        try { actual = Convert.FromHexString(parts[2]); } catch (FormatException) { return false; }
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)) return false;

        SessionClaims? claims;
        try { claims = JsonSerializer.Deserialize<SessionClaims>(Base64UrlDecode(parts[1])); }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return false; }
        if (claims is null || claims.Version != 2 || string.IsNullOrWhiteSpace(claims.SessionId) ||
            string.IsNullOrWhiteSpace(claims.TenantId) || string.IsNullOrWhiteSpace(claims.WorkspaceId) ||
            string.IsNullOrWhiteSpace(claims.PrincipalId) || string.IsNullOrWhiteSpace(claims.Audience) ||
            claims.SessionId.Length > 256 || claims.TenantId.Length > 256 || claims.WorkspaceId.Length > 256 ||
            claims.PrincipalId.Length > 256 || claims.Audience.Length > 128 || claims.Grants is null || claims.Grants.Length > 64 ||
            claims.Grants.Any(static grant => string.IsNullOrWhiteSpace(grant) || grant.Length > 128) ||
            !Enum.IsDefined(claims.PrincipalKind) || !Enum.IsDefined(claims.Assurance) ||
            (expectedAudience is not null && !string.Equals(claims.Audience, expectedAudience, StringComparison.Ordinal))) return false;
        DateTimeOffset issuedAt;
        DateTimeOffset expiry;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(claims.IssuedAtUnixSeconds);
            expiry = DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException) { return false; }
        var now = DateTimeOffset.UtcNow;
        if (expiry <= now || issuedAt > now.AddMinutes(5) || expiry <= issuedAt || _revoked.ContainsKey(claims.SessionId)) return false;
        var grants = (claims.Grants ?? [])
            .Where(static grant => !string.IsNullOrWhiteSpace(grant))
            .ToHashSet(StringComparer.Ordinal);
        context = new RequestContext(
            new(claims.TenantId),
            new(claims.WorkspaceId),
            new(claims.PrincipalId, claims.PrincipalKind),
            claims.SessionId,
            claims.Assurance,
            Guid.NewGuid().ToString("N"),
            null,
            grants);
        expiresAt = expiry;
        return true;
    }
    public void Revoke(string sessionId) => _revoked[sessionId] = DateTimeOffset.UtcNow;

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw new FormatException() };
        return Convert.FromBase64String(padded);
    }

    private sealed record SessionClaims(
        int Version,
        string SessionId,
        string TenantId,
        string WorkspaceId,
        string PrincipalId,
        PrincipalKind PrincipalKind,
        AuthAssurance Assurance,
        string Audience,
        string[] Grants,
        long IssuedAtUnixSeconds,
        long ExpiresAtUnixSeconds);
}

public sealed record SessionPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    DateTimeOffset AccessExpiresAt = default,
    string Audience = SessionAudiences.Mcp);
public interface ISessionManager
{
    SessionPair Create(RequestContext context, TimeSpan accessLifetime, string audience = SessionAudiences.Mcp);
    bool TryRefresh(string refreshToken, TimeSpan accessLifetime, out SessionPair pair);
    bool TryRefresh(string refreshToken, TimeSpan accessLifetime, string expectedAudience, out SessionPair pair);
    bool Revoke(string refreshToken);
    bool Revoke(string refreshToken, string expectedAudience);
}

/// <summary>One-use refresh rotation and revocation for signed sessions. Store this behind a durable repository in production.</summary>
public sealed class SessionManager : ISessionManager
{
    private readonly SessionTokenService _tokens;
    private readonly TimeSpan _refreshLifetime;
    private readonly ConcurrentDictionary<string, RefreshEntry> _refresh = new(StringComparer.Ordinal);

    public SessionManager(SessionTokenService tokens, TimeSpan? refreshLifetime = null)
    {
        _tokens = tokens;
        _refreshLifetime = refreshLifetime ?? TimeSpan.FromDays(30);
    }

    public SessionPair Create(RequestContext context, TimeSpan accessLifetime, string audience = SessionAudiences.Mcp)
    {
        var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.Add(_refreshLifetime);
        var accessExpires = DateTimeOffset.UtcNow.Add(accessLifetime);
        _refresh[Hash(refresh)] = new RefreshEntry(context, expires, audience);
        return new SessionPair(_tokens.Issue(context, accessLifetime, audience), refresh, expires, accessExpires, audience);
    }

    public bool TryRefresh(string refreshToken, TimeSpan accessLifetime, out SessionPair pair)
        => TryRefreshCore(refreshToken, accessLifetime, expectedAudience: null, out pair);

    public bool TryRefresh(string refreshToken, TimeSpan accessLifetime, string expectedAudience, out SessionPair pair)
        => TryRefreshCore(refreshToken, accessLifetime, expectedAudience, out pair);

    private bool TryRefreshCore(string refreshToken, TimeSpan accessLifetime, string? expectedAudience, out SessionPair pair)
    {
        pair = default!;
        var key = Hash(refreshToken);
        if (!_refresh.TryGetValue(key, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow ||
            (expectedAudience is not null && !string.Equals(entry.Audience, expectedAudience, StringComparison.Ordinal)) ||
            !_refresh.TryRemove(key, out entry)) return false;
        pair = Create(entry.Context with { CorrelationId = Guid.NewGuid().ToString("N") }, accessLifetime, entry.Audience);
        return true;
    }

    public bool Revoke(string refreshToken)
        => RevokeCore(refreshToken, expectedAudience: null);

    public bool Revoke(string refreshToken, string expectedAudience)
        => RevokeCore(refreshToken, expectedAudience);

    private bool RevokeCore(string refreshToken, string? expectedAudience)
    {
        var hash = Hash(refreshToken);
        if (!_refresh.TryGetValue(hash, out var entry) ||
            (expectedAudience is not null && !string.Equals(entry.Audience, expectedAudience, StringComparison.Ordinal)) ||
            !_refresh.TryRemove(hash, out entry)) return false;
        _tokens.Revoke(entry.Context.SessionId);
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    private sealed record RefreshEntry(RequestContext Context, DateTimeOffset ExpiresAt, string Audience);
}

public sealed class FileSessionManager : ISessionManager
{
    private readonly SessionTokenService _tokens;
    private readonly TimeSpan _lifetime;
    private readonly AuthenticatedJsonLinesJournal _journal;
    private readonly Dictionary<string, RefreshEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public FileSessionManager(
        SessionTokenService tokens,
        string path,
        TimeSpan? refreshLifetime = null,
        byte[]? journalIntegrityKey = null)
        : this(tokens, path, refreshLifetime, journalIntegrityKey, null)
    {
    }

    internal FileSessionManager(
        AuthenticatedJournalFaultInjection journalFaultInjection,
        SessionTokenService tokens,
        string path,
        TimeSpan? refreshLifetime = null,
        byte[]? journalIntegrityKey = null)
        : this(tokens, path, refreshLifetime, journalIntegrityKey, journalFaultInjection)
    {
    }

    private FileSessionManager(
        SessionTokenService tokens,
        string path,
        TimeSpan? refreshLifetime,
        byte[]? journalIntegrityKey,
        AuthenticatedJournalFaultInjection? journalFaultInjection)
    {
        if (journalIntegrityKey is not { Length: >= 32 })
            throw new ArgumentException("A stable journal integrity key of at least 256 bits is required.", nameof(journalIntegrityKey));
        _tokens = tokens;
        _lifetime = refreshLifetime ?? TimeSpan.FromDays(30);
        _journal = new AuthenticatedJsonLinesJournal("digitalbrain.v2.sessions", journalIntegrityKey, path, journalFaultInjection);
        Load();
    }

    public SessionPair Create(RequestContext context, TimeSpan accessLifetime, string audience = SessionAudiences.Mcp)
    {
        lock (_gate)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var entry = new RefreshEntry(context, DateTimeOffset.UtcNow.Add(_lifetime), audience);
            var hash = Hash(token);
            var accessExpires = DateTimeOffset.UtcNow.Add(accessLifetime);
            var accessToken = _tokens.Issue(context, accessLifetime, audience);
            Append(new("create", hash, entry));
            _entries[hash] = entry;
            return new(accessToken, token, entry.ExpiresAt, accessExpires, audience);
        }
    }

    public bool TryRefresh(string refreshToken, TimeSpan accessLifetime, out SessionPair pair)
        => TryRefreshCore(refreshToken, accessLifetime, expectedAudience: null, out pair);

    public bool TryRefresh(string refreshToken, TimeSpan accessLifetime, string expectedAudience, out SessionPair pair)
        => TryRefreshCore(refreshToken, accessLifetime, expectedAudience, out pair);

    private bool TryRefreshCore(string refreshToken, TimeSpan accessLifetime, string? expectedAudience, out SessionPair pair)
    {
        lock (_gate)
        {
            pair = default!;
            var hash = Hash(refreshToken);
            if (!_entries.TryGetValue(hash, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow ||
                (expectedAudience is not null && !string.Equals(entry.Audience, expectedAudience, StringComparison.Ordinal))) return false;

            var nextToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var nextHash = Hash(nextToken);
            var nextContext = entry.Context with { CorrelationId = Guid.NewGuid().ToString("N") };
            var nextEntry = new RefreshEntry(nextContext, DateTimeOffset.UtcNow.Add(_lifetime), entry.Audience);
            var accessExpires = DateTimeOffset.UtcNow.Add(accessLifetime);
            var accessToken = _tokens.Issue(nextContext, accessLifetime, entry.Audience);
            Append(new("rotate", nextHash, nextEntry, null, hash));
            _entries.Remove(hash);
            _entries[nextHash] = nextEntry;
            pair = new(accessToken, nextToken, nextEntry.ExpiresAt, accessExpires, entry.Audience);
            return true;
        }
    }

    public bool Revoke(string refreshToken)
        => RevokeCore(refreshToken, expectedAudience: null);

    public bool Revoke(string refreshToken, string expectedAudience)
        => RevokeCore(refreshToken, expectedAudience);

    private bool RevokeCore(string refreshToken, string? expectedAudience)
    {
        lock (_gate)
        {
            var hash = Hash(refreshToken);
            if (!_entries.TryGetValue(hash, out var entry) ||
                (expectedAudience is not null && !string.Equals(entry.Audience, expectedAudience, StringComparison.Ordinal))) return false;
            Append(new("logout", hash, null, entry.Context.SessionId));
            _entries.Remove(hash);
            _tokens.Revoke(entry.Context.SessionId);
            return true;
        }
    }

    private void Load()
    {
        foreach (var record in _journal.Read())
        {
            PersistedSession? item;
            try { item = JsonSerializer.Deserialize<PersistedSession>(record.Payload); }
            catch (JsonException)
            {
                throw _journal.Invalid(record, "invalid-json", $"The session journal contains invalid JSON at line {record.LineNumber}.");
            }
            if (item is null || !IsHash(item.Hash)) Invalid(record, "invalid-record");
            if (!record.IsLegacy && !string.Equals(record.Kind, "session." + item!.Kind, StringComparison.Ordinal))
                Invalid(record, "kind-mismatch");
            if (item!.Kind == "create" && ValidEntry(item.Entry) && !_entries.ContainsKey(item.Hash))
                _entries[item.Hash] = item.Entry!;
            else if (item.Kind == "rotate" && ValidEntry(item.Entry) && IsHash(item.PreviousHash) &&
                     _entries.ContainsKey(item.PreviousHash!) && !_entries.ContainsKey(item.Hash))
            {
                _entries.Remove(item.PreviousHash!);
                _entries[item.Hash] = item.Entry!;
            }
            else if (item.Kind == "revoke" && _entries.ContainsKey(item.Hash))
                _entries.Remove(item.Hash);
            else if (item.Kind == "logout" && _entries.TryGetValue(item.Hash, out var logoutEntry) &&
                     !string.IsNullOrWhiteSpace(item.SessionId) && item.SessionId.Length <= 256 &&
                     string.Equals(logoutEntry.Context.SessionId, item.SessionId, StringComparison.Ordinal))
            {
                _entries.Remove(item.Hash);
                _tokens.Revoke(item.SessionId);
            }
            else
                Invalid(record, "invalid-transition");
        }
        _journal.SealLegacy();
        foreach (var expired in _entries.Where(static pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(static pair => pair.Key).ToArray())
            _entries.Remove(expired);
    }

    private void Invalid(AuthenticatedJournalRecord record, string reason)
    {
        throw _journal.Invalid(record, reason, $"The session journal contains an invalid transition at line {record.LineNumber}.");
    }

    private void Append(PersistedSession item)
    {
        _journal.Append("session." + item.Kind, JsonSerializer.Serialize(item));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(static character => Uri.IsHexDigit(character));
    private static bool ValidEntry(RefreshEntry? entry) => entry is not null && entry.Context is not null && entry.ExpiresAt != default &&
        !string.IsNullOrWhiteSpace(entry.Audience) && entry.Audience.Length <= 128 &&
        !string.IsNullOrWhiteSpace(entry.Context.SessionId) && entry.Context.SessionId.Length <= 256 &&
        !string.IsNullOrWhiteSpace(entry.Context.TenantId.Value) && entry.Context.TenantId.Value.Length <= 256 &&
        !string.IsNullOrWhiteSpace(entry.Context.WorkspaceId.Value) && entry.Context.WorkspaceId.Value.Length <= 256 &&
        !string.IsNullOrWhiteSpace(entry.Context.Principal.Value) && entry.Context.Principal.Value.Length <= 256 &&
        Enum.IsDefined(entry.Context.Principal.Kind) && Enum.IsDefined(entry.Context.Assurance) && entry.Context.Grants is not null &&
        entry.Context.Grants.Count <= 64 && entry.Context.Grants.All(static grant => !string.IsNullOrWhiteSpace(grant) && grant.Length <= 128);
    private sealed record RefreshEntry(RequestContext Context, DateTimeOffset ExpiresAt, string Audience = SessionAudiences.Mcp);
    private sealed record PersistedSession(
        string Kind,
        string Hash,
        RefreshEntry? Entry,
        string? SessionId = null,
        string? PreviousHash = null);
}

public enum Sensitivity { Public, Internal, Confidential, Secret }
public sealed record SensitiveValue(string Value, Sensitivity Classification);
public static class Redaction
{
    public static string SafeSummary(string? value, Sensitivity classification = Sensitivity.Internal) =>
        classification == Sensitivity.Secret ? "[REDACTED]" : value is null ? string.Empty : value.Length > 256 ? value[..256] + "…" : value;
    public static JsonElement Redact(JsonElement value, Sensitivity classification) =>
        classification == Sensitivity.Secret ? JsonElement.Parse("\"[REDACTED]\"") : value.Clone();
}

[GenerateSerializer, Alias("digitalbrain.v2.command-envelope")]
public sealed record CommandEnvelope([property: Id(0)] string Type, [property: Id(1)] int Version, [property: Id(2)] string CommandId, [property: Id(3)] RequestContext Context, [property: Id(4)] JsonElement Payload);
[GenerateSerializer, Alias("digitalbrain.v2.event-envelope")]
public sealed record EventEnvelope([property: Id(0)] string Type, [property: Id(1)] int Version, [property: Id(2)] string EventId, [property: Id(3)] string CorrelationId, [property: Id(4)] string? CausationId, [property: Id(5)] JsonElement Payload);

public enum WorkflowState { Proposed, AwaitingApproval, Approved, Rejected, Expired, Cancelled, ApplyQueued, Applying, RetryScheduled, Succeeded, Failed, OutcomeUnknown, CompensationQueued, Compensated, ManualIntervention, AwaitingExternalAuthorization }
[GenerateSerializer, Alias("digitalbrain.v2.workflow-transition")]
public sealed record WorkflowTransition([property: Id(0)] WorkflowState From, [property: Id(1)] WorkflowState To, [property: Id(2)] DateTimeOffset At, [property: Id(3)] string? Reason = null);
[GenerateSerializer, Alias("digitalbrain.v2.approval-record")]
public sealed record ApprovalRecord([property: Id(0)] PrincipalRef Approver, [property: Id(1)] DateTimeOffset ApprovedAt, [property: Id(2)] string DecisionId, [property: Id(3)] string? Reason);
[GenerateSerializer, Alias("digitalbrain.v2.aggregate-commit")]
public sealed record AggregateCommit([property: Id(0)] long CommitSequence, [property: Id(1)] string CommitId, [property: Id(2)] IReadOnlyList<EventEnvelope> Events, [property: Id(3)] string Checksum, [property: Id(4)] DateTimeOffset CommittedAt);
[GenerateSerializer, Alias("digitalbrain.v2.outbox-record")]
public sealed record OutboxRecord([property: Id(0)] string EffectId, [property: Id(1)] string OperationId, [property: Id(2)] int Ordinal, [property: Id(3)] string EffectType, [property: Id(4)] JsonElement Intent, [property: Id(5)] DateTimeOffset Deadline);
[GenerateSerializer, Alias("digitalbrain.v2.effect-transition")]
public sealed record EffectTransitionRecord(
    [property: Id(0)] string EffectId,
    [property: Id(1)] string TransitionId,
    [property: Id(2)] string State,
    [property: Id(3)] string? SafeResult,
    [property: Id(4)] DateTimeOffset At,
    [property: Id(5)] string? LeaseOwner = null,
    [property: Id(6)] DateTimeOffset? LeaseExpiresAt = null,
    [property: Id(7)] string? ProviderOperationId = null);

public static class CommitSeal
{
    public static string Compute(IEnumerable<EventEnvelope> events) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(events))));
}

public sealed class Workflow
{
    public WorkflowState State { get; private set; } = WorkflowState.Proposed;
    public ApprovalRecord? Approval { get; private set; }
    public IReadOnlyList<WorkflowTransition> Transitions => _transitions;
    private readonly List<WorkflowTransition> _transitions = [];
    public void SubmitForApproval() => Transition(WorkflowState.AwaitingApproval);
    public void Approve(ApprovalRecord approval)
    {
        if (State != WorkflowState.AwaitingApproval) throw new InvalidOperationException($"Approval is only legal while awaiting approval; current state is {State}.");
        if (approval.Approver.Kind != PrincipalKind.Operator || string.IsNullOrWhiteSpace(approval.Approver.Value)) throw new UnauthorizedAccessException();
        Approval = approval;
        Transition(WorkflowState.Approved);
        Transition(WorkflowState.ApplyQueued);
    }
    public void Reject(string reason)
    {
        if (State != WorkflowState.AwaitingApproval) throw new InvalidOperationException($"Rejection is only legal while awaiting approval; current state is {State}.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A safe rejection reason is required.", nameof(reason));
        Transition(WorkflowState.Rejected);
    }
    public void Expire()
    {
        if (State != WorkflowState.AwaitingApproval) throw new InvalidOperationException($"Expiry is only legal while awaiting approval; current state is {State}.");
        Transition(WorkflowState.Expired);
    }
    public void Cancel()
    {
        if (State is not (WorkflowState.Proposed or WorkflowState.AwaitingApproval or WorkflowState.ApplyQueued)) throw new InvalidOperationException($"Cancellation is not legal from {State}.");
        Transition(WorkflowState.Cancelled);
    }
    public void BeginApply() => Transition(WorkflowState.Applying);
    public void Succeed() => Transition(WorkflowState.Succeeded);
    public void Unknown() => Transition(WorkflowState.OutcomeUnknown);
    public void Compensate() { Transition(WorkflowState.CompensationQueued); Transition(WorkflowState.Compensated); }
    private void Transition(WorkflowState next)
    {
        if (State is WorkflowState.Succeeded or WorkflowState.Compensated or WorkflowState.Rejected or WorkflowState.Cancelled) throw new InvalidOperationException($"Workflow is terminal: {State}");
        var prior = State;
        State = next;
        _transitions.Add(new WorkflowTransition(prior, next, DateTimeOffset.UtcNow));
    }
}

public sealed class CapabilityIsolationGate
{
    public bool IsAllowed(RequestContext context, TenantId tenant, WorkspaceId workspace, string capability) =>
        context.TenantId == tenant && context.WorkspaceId == workspace && context.Grants.Contains(capability);
    public void Demand(RequestContext context, TenantId tenant, WorkspaceId workspace, string capability)
    {
        if (!IsAllowed(context, tenant, workspace, capability)) throw new UnauthorizedAccessException("Capability denied.");
    }
}
