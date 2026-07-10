using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orleans;

namespace DigitalBrain.Core.V2;

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

[GenerateSerializer, Alias("digitalbrain.v2.persisted-actor-snapshot")]
public sealed record PersistedActorSnapshot(
    [property: Id(0)] TenantId TenantId,
    [property: Id(1)] WorkspaceId WorkspaceId,
    [property: Id(2)] PrincipalRef Principal,
    [property: Id(3)] AuthAssurance Assurance,
    [property: Id(4)] DateTimeOffset CapturedAt);

public static class V2GrainIds
{
    public static string Aggregate(TenantId tenant, WorkspaceId workspace, string aggregate) =>
        $"v2:{tenant.Value}:{workspace.Value}:aggregate:{aggregate}";
    public static string Conversation(TenantId tenant, WorkspaceId workspace, string conversation) =>
        $"v2:{tenant.Value}:{workspace.Value}:conversation:{conversation}";
    public static string Workflow(TenantId tenant, WorkspaceId workspace, string workflow) =>
        $"v2:{tenant.Value}:{workspace.Value}:workflow:{workflow}";
}

public sealed class V2SessionTokenService
{
    private readonly byte[] _key;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new(StringComparer.Ordinal);
    public V2SessionTokenService(byte[] key)
    {
        if (key.Length < 32) throw new ArgumentException("V2 session signing key must be at least 256 bits.", nameof(key));
        _key = key.ToArray();
    }
    public string Issue(RequestContext context, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var body = string.Join(".", "v2", context.SessionId, context.TenantId.Value, context.WorkspaceId.Value, context.Principal.Value, (int)context.Principal.Kind, (int)context.Assurance, expires.ToUnixTimeSeconds());
        var sig = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body)));
        return body + "." + sig;
    }
    public bool TryValidate(string token, out RequestContext context)
    {
        context = default!;
        var parts = token.Split('.');
        if (parts.Length != 9 || parts[0] != "v2" || !int.TryParse(parts[5], out var kind) || !Enum.IsDefined(typeof(PrincipalKind), kind) || !int.TryParse(parts[6], out var assurance) || !Enum.IsDefined(typeof(AuthAssurance), assurance) || !long.TryParse(parts[7], out var seconds)) return false;
        var body = string.Join('.', parts.Take(8));
        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body));
        byte[] actual;
        try { actual = Convert.FromHexString(parts[8]); } catch (FormatException) { return false; }
        DateTimeOffset expiry;
        try { expiry = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected) || expiry <= DateTimeOffset.UtcNow || _revoked.ContainsKey(parts[1])) return false;
        context = new RequestContext(new(parts[2]), new(parts[3]), new(parts[4], (PrincipalKind)kind), parts[1], (AuthAssurance)assurance, Guid.NewGuid().ToString("N"), null, new HashSet<string>(StringComparer.Ordinal) { "brain.read" });
        return true;
    }
    public void Revoke(string sessionId) => _revoked[sessionId] = DateTimeOffset.UtcNow;
}

public sealed record V2SessionPair(string AccessToken, string RefreshToken, DateTimeOffset RefreshExpiresAt);
public interface IV2SessionManager
{
    V2SessionPair Create(RequestContext context, TimeSpan accessLifetime);
    bool TryRefresh(string refreshToken, TimeSpan accessLifetime, out V2SessionPair pair);
    bool Revoke(string refreshToken);
}

/// <summary>One-use refresh rotation and revocation for V2 sessions. Store this behind a durable repository in production.</summary>
public sealed class V2SessionManager : IV2SessionManager
{
    private readonly V2SessionTokenService _tokens;
    private readonly TimeSpan _refreshLifetime;
    private readonly ConcurrentDictionary<string, RefreshEntry> _refresh = new(StringComparer.Ordinal);

    public V2SessionManager(V2SessionTokenService tokens, TimeSpan? refreshLifetime = null)
    {
        _tokens = tokens;
        _refreshLifetime = refreshLifetime ?? TimeSpan.FromDays(30);
    }

    public V2SessionPair Create(RequestContext context, TimeSpan accessLifetime)
    {
        var refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.Add(_refreshLifetime);
        _refresh[Hash(refresh)] = new RefreshEntry(context, expires);
        return new V2SessionPair(_tokens.Issue(context, accessLifetime), refresh, expires);
    }

    public bool TryRefresh(string refreshToken, TimeSpan accessLifetime, out V2SessionPair pair)
    {
        pair = default!;
        var key = Hash(refreshToken);
        if (!_refresh.TryRemove(key, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow) return false;
        pair = Create(entry.Context with { CorrelationId = Guid.NewGuid().ToString("N") }, accessLifetime);
        return true;
    }

    public bool Revoke(string refreshToken)
    {
        if (!_refresh.TryRemove(Hash(refreshToken), out var entry)) return false;
        _tokens.Revoke(entry.Context.SessionId);
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    private sealed record RefreshEntry(RequestContext Context, DateTimeOffset ExpiresAt);
}

public sealed class FileV2SessionManager : IV2SessionManager
{
    private readonly V2SessionTokenService _tokens;
    private readonly TimeSpan _lifetime;
    private readonly string _path;
    private readonly Dictionary<string, RefreshEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public FileV2SessionManager(V2SessionTokenService tokens, string path, TimeSpan? refreshLifetime = null)
    {
        _tokens = tokens;
        _lifetime = refreshLifetime ?? TimeSpan.FromDays(30);
        _path = path;
        Load();
    }

    public V2SessionPair Create(RequestContext context, TimeSpan accessLifetime)
    {
        lock (_gate)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var entry = new RefreshEntry(context, DateTimeOffset.UtcNow.Add(_lifetime));
            _entries[Hash(token)] = entry;
            Append(new("create", Hash(token), entry));
            return new(_tokens.Issue(context, accessLifetime), token, entry.ExpiresAt);
        }
    }

    public bool TryRefresh(string refreshToken, TimeSpan accessLifetime, out V2SessionPair pair)
    {
        lock (_gate)
        {
            pair = default!;
            var hash = Hash(refreshToken);
            if (!_entries.Remove(hash, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow) return false;
            Append(new("revoke", hash, null));
            pair = Create(entry.Context with { CorrelationId = Guid.NewGuid().ToString("N") }, accessLifetime);
            return true;
        }
    }

    public bool Revoke(string refreshToken)
    {
        lock (_gate)
        {
            var hash = Hash(refreshToken);
            if (!_entries.Remove(hash, out var entry)) return false;
            Append(new("revoke", hash, null));
            _tokens.Revoke(entry.Context.SessionId);
            return true;
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        foreach (var line in File.ReadLines(_path))
        {
            var item = JsonSerializer.Deserialize<PersistedSession>(line);
            if (item is null) continue;
            if (item.Kind == "create" && item.Entry is not null) _entries[item.Hash] = item.Entry;
            else if (item.Kind == "revoke") _entries.Remove(item.Hash);
        }
    }

    private void Append(PersistedSession item)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.AppendAllText(_path, JsonSerializer.Serialize(item) + Environment.NewLine);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    private sealed record RefreshEntry(RequestContext Context, DateTimeOffset ExpiresAt);
    private sealed record PersistedSession(string Kind, string Hash, RefreshEntry? Entry);
}

public enum Sensitivity { Public, Internal, Confidential, Secret }
public sealed record SensitiveValue(string Value, Sensitivity Classification);
public static class V2Redaction
{
    public static string SafeSummary(string? value, Sensitivity classification = Sensitivity.Internal) =>
        classification == Sensitivity.Secret ? "[REDACTED]" : value is null ? string.Empty : value.Length > 256 ? value[..256] + "…" : value;
    public static JsonElement Redact(JsonElement value, Sensitivity classification) =>
        classification == Sensitivity.Secret ? JsonDocument.Parse("\"[REDACTED]\"").RootElement.Clone() : value.Clone();
}

[GenerateSerializer, Alias("digitalbrain.v2.command-envelope")]
public sealed record V2CommandEnvelope([property: Id(0)] string Type, [property: Id(1)] int Version, [property: Id(2)] string CommandId, [property: Id(3)] RequestContext Context, [property: Id(4)] JsonElement Payload);
[GenerateSerializer, Alias("digitalbrain.v2.event-envelope")]
public sealed record V2EventEnvelope([property: Id(0)] string Type, [property: Id(1)] int Version, [property: Id(2)] string EventId, [property: Id(3)] string CorrelationId, [property: Id(4)] string? CausationId, [property: Id(5)] JsonElement Payload);

public enum WorkflowState { Proposed, AwaitingApproval, Approved, Rejected, Expired, Cancelled, ApplyQueued, Applying, RetryScheduled, Succeeded, Failed, OutcomeUnknown, CompensationQueued, Compensated, ManualIntervention }
[GenerateSerializer, Alias("digitalbrain.v2.workflow-transition")]
public sealed record WorkflowTransition([property: Id(0)] WorkflowState From, [property: Id(1)] WorkflowState To, [property: Id(2)] DateTimeOffset At, [property: Id(3)] string? Reason = null);
[GenerateSerializer, Alias("digitalbrain.v2.approval-record")]
public sealed record ApprovalRecord([property: Id(0)] PrincipalRef Approver, [property: Id(1)] DateTimeOffset ApprovedAt, [property: Id(2)] string DecisionId, [property: Id(3)] string? Reason);
[GenerateSerializer, Alias("digitalbrain.v2.aggregate-commit")]
public sealed record AggregateCommit([property: Id(0)] long CommitSequence, [property: Id(1)] string CommitId, [property: Id(2)] IReadOnlyList<V2EventEnvelope> Events, [property: Id(3)] string Checksum, [property: Id(4)] DateTimeOffset CommittedAt);
[GenerateSerializer, Alias("digitalbrain.v2.outbox-record")]
public sealed record OutboxRecord([property: Id(0)] string EffectId, [property: Id(1)] string OperationId, [property: Id(2)] int Ordinal, [property: Id(3)] string EffectType, [property: Id(4)] JsonElement Intent, [property: Id(5)] DateTimeOffset Deadline);
[GenerateSerializer, Alias("digitalbrain.v2.effect-transition")]
public sealed record EffectTransitionRecord([property: Id(0)] string EffectId, [property: Id(1)] string TransitionId, [property: Id(2)] string State, [property: Id(3)] string? SafeResult, [property: Id(4)] DateTimeOffset At);

public static class V2CommitSeal
{
    public static string Compute(IEnumerable<V2EventEnvelope> events) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(events))));
}

public sealed class V2Workflow
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
        if (!IsAllowed(context, tenant, workspace, capability)) throw new UnauthorizedAccessException("V2 capability denied.");
    }
}
