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
    [property: Id(7)] IReadOnlySet<string> Grants,
    [property: Id(8)] string? ConversationId = null);

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
    private const string StructuredPrefix = "v3s";
    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;
    public SessionTokenService(byte[] key, TimeProvider? timeProvider = null)
    {
        if (key.Length < 32) throw new ArgumentException("The session signing key must be at least 256 bits.", nameof(key));
        _key = key.ToArray();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }
    public string Issue(
        RequestContext context,
        TimeSpan lifetime,
        string audience = SessionAudiences.Mcp,
        long sessionVersion = 1)
    {
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (string.IsNullOrWhiteSpace(audience)) throw new ArgumentException("A non-empty session audience is required.", nameof(audience));
        if (sessionVersion < 1) throw new ArgumentOutOfRangeException(nameof(sessionVersion));
        if (string.IsNullOrWhiteSpace(context.SessionId) || string.IsNullOrWhiteSpace(context.TenantId.Value) ||
            string.IsNullOrWhiteSpace(context.WorkspaceId.Value) || string.IsNullOrWhiteSpace(context.Principal.Value))
            throw new ArgumentException("A complete request context is required.", nameof(context));
        if (context.SessionId.Length > 256 || context.TenantId.Value.Length > 256 || context.WorkspaceId.Value.Length > 256 ||
            context.Principal.Value.Length > 256 || audience.Length > 128 || context.Grants.Count > 64 ||
            context.Grants.Any(static grant => string.IsNullOrWhiteSpace(grant) || grant.Length > 128))
            throw new ArgumentException("Session claims exceed the signed transport bound.", nameof(context));

        var now = _timeProvider.GetUtcNow();
        var claims = new SessionClaims(
            3,
            context.SessionId,
            sessionVersion,
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
        => TryValidateCore(token, expectedAudience: null, out context, out _, out _);

    public bool TryValidate(string token, string expectedAudience, out RequestContext context)
    {
        context = default!;
        return !string.IsNullOrWhiteSpace(expectedAudience) && TryValidateCore(token, expectedAudience, out context, out _, out _);
    }

    public bool TryValidate(string token, string expectedAudience, out RequestContext context, out DateTimeOffset expiresAt)
    {
        context = default!;
        expiresAt = default;
        return !string.IsNullOrWhiteSpace(expectedAudience) && TryValidateCore(token, expectedAudience, out context, out expiresAt, out _);
    }

    public bool TryValidate(
        string token,
        string expectedAudience,
        out RequestContext context,
        out DateTimeOffset expiresAt,
        out long sessionVersion)
    {
        context = default!;
        expiresAt = default;
        sessionVersion = 0;
        return !string.IsNullOrWhiteSpace(expectedAudience) &&
               TryValidateCore(token, expectedAudience, out context, out expiresAt, out sessionVersion);
    }

    private bool TryValidateCore(
        string token,
        string? expectedAudience,
        out RequestContext context,
        out DateTimeOffset expiresAt,
        out long sessionVersion)
    {
        context = default!;
        expiresAt = default;
        sessionVersion = 0;
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
        if (claims is null || claims.Version != 3 || claims.SessionVersion < 1 || string.IsNullOrWhiteSpace(claims.SessionId) ||
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
        var now = _timeProvider.GetUtcNow();
        if (expiry <= now || issuedAt > now.AddMinutes(5) || expiry <= issuedAt) return false;
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
        sessionVersion = claims.SessionVersion;
        return true;
    }

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
        long SessionVersion,
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
