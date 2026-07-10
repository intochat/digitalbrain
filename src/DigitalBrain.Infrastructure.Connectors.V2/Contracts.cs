using DigitalBrain.Core.V2;
using Orleans;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Infrastructure.Connectors.V2;

public static class V2ConnectorSchema
{
    public const int Version = 2;
    public const string StorageNamespace = "digitalbrain-v2-connectors";
}

[GenerateSerializer, Alias("digitalbrain.v2.connector.credential-owner")]
public readonly record struct CredentialOwner(
    [property: Id(0)] TenantId TenantId,
    [property: Id(1)] WorkspaceId WorkspaceId,
    [property: Id(2)] PrincipalRef Principal)
{
    public static CredentialOwner From(V2RequestContext context) => new(context.TenantId, context.WorkspaceId, context.Principal);
}

[GenerateSerializer, Alias("digitalbrain.v2.connector.credential-ref")]
public readonly record struct CredentialRef([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer, Alias("digitalbrain.v2.connector.secret-ref")]
public readonly record struct SecretRef(
    [property: Id(0)] string Value,
    [property: Id(1)] int Version,
    [property: Id(2)] string Purpose)
{
    public override string ToString() => $"{Purpose}:{Value}:v{Version}";
}

[GenerateSerializer, Alias("digitalbrain.v2.connector.oauth-flow-key")]
public readonly record struct OAuthFlowKey(
    [property: Id(0)] int KeyVersion,
    [property: Id(1)] string Digest)
{
    public override string ToString() => $"v2:oauth:{KeyVersion}:{Digest}";
}

public enum OAuthFlowStatus
{
    Started,
    Claimed,
    ExchangeQueued,
    Exchanging,
    RetryScheduled,
    Succeeded,
    Failed,
    OutcomeUnknown,
    ReauthorizationRequired,
    Expired,
    Revoked
}

public enum OAuthEffectKind
{
    Exchange,
    Refresh,
    Revoke
}

public enum CredentialStatus
{
    Connected,
    Refreshing,
    Revoking,
    Expired,
    Revoked,
    OutcomeUnknown,
    ReauthorizationRequired,
    Unavailable
}

public enum ConnectorStatusKind
{
    Connected,
    NeedsAuth,
    InsufficientGrant,
    Expired,
    Revoked,
    ReauthorizationRequired,
    Unavailable
}

public enum ConnectorRiskClass
{
    Read,
    Write,
    ExternalSideEffect
}

[GenerateSerializer, Alias("digitalbrain.v2.connector.capability-descriptor")]
public sealed record ConnectorCapabilityDescriptor(
    [property: Id(0)] string Id,
    [property: Id(1)] int Version,
    [property: Id(2)] string Provider,
    [property: Id(3)] IReadOnlyList<string> RequiredScopes,
    [property: Id(4)] ConnectorRiskClass Risk,
    [property: Id(5)] bool RequiresApproval,
    [property: Id(6)] bool IsIdempotent,
    [property: Id(7)] string DataClassification);

[GenerateSerializer, Alias("digitalbrain.v2.connector.status")]
public sealed record ConnectorStatus(
    [property: Id(0)] string Provider,
    [property: Id(1)] ConnectorStatusKind Status,
    [property: Id(2)] IReadOnlyList<string> AvailableCapabilities,
    [property: Id(3)] IReadOnlyList<string> MissingScopes,
    [property: Id(4)] DateTimeOffset CheckedAt,
    [property: Id(5)] string? SafeReason = null);

[GenerateSerializer, Alias("digitalbrain.v2.connector.oauth-transition")]
public sealed record OAuthTransition(
    [property: Id(0)] long Sequence,
    [property: Id(1)] OAuthFlowStatus Status,
    [property: Id(2)] DateTimeOffset At,
    [property: Id(3)] string? SafeReason = null);

[GenerateSerializer, Alias("digitalbrain.v2.connector.oauth-effect-intent")]
public sealed record OAuthEffectIntent(
    [property: Id(0)] string EffectId,
    [property: Id(1)] OAuthEffectKind Kind,
    [property: Id(2)] string Provider,
    [property: Id(3)] OAuthFlowKey FlowKey,
    [property: Id(4)] SecretRef? CodeRef,
    [property: Id(5)] SecretRef? VerifierRef,
    [property: Id(6)] CredentialRef? CredentialRef,
    [property: Id(7)] int Attempt,
    [property: Id(8)] DateTimeOffset DueAt,
    [property: Id(9)] DateTimeOffset Deadline,
    [property: Id(10)] string CorrelationId);

[GenerateSerializer, Alias("digitalbrain.v2.connector.oauth-flow-record")]
public sealed record OAuthFlowRecord(
    [property: Id(0)] OAuthFlowKey Key,
    [property: Id(1)] long Revision,
    [property: Id(2)] OAuthFlowStatus Status,
    [property: Id(3)] string Provider,
    [property: Id(4)] CredentialOwner Owner,
    [property: Id(5)] string RedirectUri,
    [property: Id(6)] IReadOnlyList<string> RequestedScopes,
    [property: Id(7)] IReadOnlyList<string> RequestedCapabilities,
    [property: Id(8)] SecretRef VerifierRef,
    [property: Id(9)] SecretRef? CodeRef,
    [property: Id(10)] string? EffectId,
    [property: Id(11)] CredentialRef? ResultCredentialRef,
    [property: Id(12)] DateTimeOffset StartedAt,
    [property: Id(13)] DateTimeOffset ExpiresAt,
    [property: Id(14)] string CorrelationId,
    [property: Id(15)] IReadOnlyList<OAuthTransition> Transitions,
    [property: Id(16)] string? LeaseOwner,
    [property: Id(17)] DateTimeOffset? LeaseExpiresAt,
    [property: Id(18)] DateTimeOffset? NextAttemptAt,
    [property: Id(19)] string? SafeFailure);

[GenerateSerializer, Alias("digitalbrain.v2.connector.credential-record")]
public sealed record CredentialRecord(
    [property: Id(0)] CredentialRef Reference,
    [property: Id(1)] long Revision,
    [property: Id(2)] string Provider,
    [property: Id(3)] CredentialOwner Owner,
    [property: Id(4)] CredentialStatus Status,
    [property: Id(5)] SecretRef ActiveSecret,
    [property: Id(6)] IReadOnlyList<string> GrantedScopes,
    [property: Id(7)] string ProviderAccountHash,
    [property: Id(8)] Uri? ResourceBaseUri,
    [property: Id(9)] DateTimeOffset CreatedAt,
    [property: Id(10)] DateTimeOffset UpdatedAt,
    [property: Id(11)] DateTimeOffset? AccessTokenExpiresAt,
    [property: Id(12)] string? LeaseOwner,
    [property: Id(13)] DateTimeOffset? LeaseExpiresAt,
    [property: Id(14)] string? SafeFailure);

/// <summary>
/// Ephemeral secret material. It is deliberately not serializer-enabled and never exposes values from ToString().
/// </summary>
public sealed class SecretPayload
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public SecretPayload(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool TryGetValue(string name, out string value) => _values.TryGetValue(name, out value!);

    public SecretPayload WithMissingValuesFrom(SecretPayload prior, params string[] keys)
    {
        var merged = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if ((!merged.TryGetValue(key, out var current) || string.IsNullOrWhiteSpace(current)) &&
                prior.TryGetValue(key, out var previous) && !string.IsNullOrWhiteSpace(previous))
            {
                merged[key] = previous;
            }
        }

        return new SecretPayload(merged);
    }

    public override string ToString() => "[SECRET]";
}

public sealed record OAuthAuthorizationRequest(
    string State,
    string RedirectUri,
    string CodeChallenge,
    IReadOnlyList<string> Scopes);

public sealed record OAuthExchangeRequest(
    string Code,
    string CodeVerifier,
    string RedirectUri,
    IReadOnlyList<string> RequestedScopes);

public sealed record OAuthRefreshRequest(
    CredentialRef CredentialRef,
    SecretPayload CurrentSecret,
    IReadOnlyList<string> GrantedScopes);

public sealed record OAuthRevokeRequest(CredentialRef CredentialRef, SecretPayload CurrentSecret);

public sealed record ProviderTokenSet(
    SecretPayload Secret,
    IReadOnlyList<string> GrantedScopes,
    string TokenType,
    string ProviderAccountId,
    DateTimeOffset? AccessTokenExpiresAt,
    Uri? ResourceBaseUri = null);

public enum ProviderCallOutcome
{
    Success,
    RetryableFailure,
    PermanentFailure,
    OutcomeUnknown,
    ReauthorizationRequired
}

public sealed record ProviderCallResult<T>(
    ProviderCallOutcome Outcome,
    T? Value = default,
    string? SafeReason = null,
    bool ProviderCommitPointReached = false)
{
    public static ProviderCallResult<T> Success(T value) => new(ProviderCallOutcome.Success, value);
    public static ProviderCallResult<T> Retryable(string reason) => new(ProviderCallOutcome.RetryableFailure, default, reason);
    public static ProviderCallResult<T> Permanent(string reason) => new(ProviderCallOutcome.PermanentFailure, default, reason);
    public static ProviderCallResult<T> Unknown(string reason) => new(ProviderCallOutcome.OutcomeUnknown, default, reason, true);
    public static ProviderCallResult<T> Reauthorize(string reason) => new(ProviderCallOutcome.ReauthorizationRequired, default, reason);
}

public interface IProviderOAuthAdapter
{
    string ProviderId { get; }
    IReadOnlyList<ConnectorCapabilityDescriptor> Capabilities { get; }
    bool IsAllowedRedirectUri(string redirectUri);
    Uri CreateAuthorizationUri(OAuthAuthorizationRequest request);
    Task<ProviderCallResult<ProviderTokenSet>> ExchangeAsync(OAuthExchangeRequest request, CancellationToken cancellationToken);
    Task<ProviderCallResult<ProviderTokenSet>> RefreshAsync(OAuthRefreshRequest request, CancellationToken cancellationToken);
    Task<ProviderCallResult<bool>> RevokeAsync(OAuthRevokeRequest request, CancellationToken cancellationToken);
}

public interface IProviderOAuthAdapterRegistry
{
    IProviderOAuthAdapter GetRequired(string provider);
}

public interface IConnectorAuthorizationPolicy
{
    void DemandAuthorize(V2RequestContext context, string provider, IReadOnlyList<string> capabilityIds);
    void DemandUse(V2RequestContext context, CredentialRecord credential, ConnectorCapabilityDescriptor capability);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface ISecretVault
{
    Task<SecretRef> WriteAsync(CredentialOwner owner, string purpose, SecretPayload payload, DateTimeOffset? expiresAt, CancellationToken cancellationToken);
    Task<SecretPayload?> ReadAsync(CredentialOwner owner, SecretRef reference, CancellationToken cancellationToken);
    Task RetireAsync(CredentialOwner owner, SecretRef reference, CancellationToken cancellationToken);
}

public interface ICredentialMetadataRepository
{
    Task<CredentialRecord?> GetAsync(CredentialRef reference, CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(CredentialRecord record, CancellationToken cancellationToken);
    Task<bool> TryReplaceAsync(CredentialRecord record, long expectedRevision, CancellationToken cancellationToken);
}

public interface IV2CredentialStore
{
    Task<CredentialRecord?> GetAsync(CredentialOwner owner, CredentialRef reference, CancellationToken cancellationToken);
    Task<SecretPayload?> ReadSecretAsync(CredentialOwner owner, CredentialRef reference, CancellationToken cancellationToken);
    Task<CredentialRecord> CreateFromExchangeAsync(CredentialRef reference, string provider, CredentialOwner owner, ProviderTokenSet tokenSet, CancellationToken cancellationToken);
    Task<CredentialRecord?> TryAcquireLeaseAsync(CredentialOwner owner, CredentialRef reference, string leaseOwner, TimeSpan duration, CredentialStatus operationStatus, CancellationToken cancellationToken);
    Task<CredentialRecord> RotateAsync(CredentialOwner owner, CredentialRef reference, long expectedRevision, ProviderTokenSet tokenSet, bool retainMissingRefreshToken, CancellationToken cancellationToken);
    Task MarkStatusAsync(CredentialOwner owner, CredentialRef reference, long expectedRevision, CredentialStatus status, string? safeFailure, CancellationToken cancellationToken);
    Task MarkRevokedAsync(CredentialOwner owner, CredentialRef reference, long expectedRevision, CancellationToken cancellationToken);
}

public interface IOAuthFlowStore
{
    Task<bool> TryCreateAsync(OAuthFlowRecord flow, CancellationToken cancellationToken);
    Task<OAuthFlowRecord?> GetAsync(OAuthFlowKey key, CancellationToken cancellationToken);
    Task<OAuthFlowRecord> ClaimAndEnqueueAsync(OAuthFlowKey key, long expectedRevision, SecretRef codeRef, OAuthEffectIntent effect, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OAuthFlowRecord?> TryAcquireExchangeLeaseAsync(OAuthFlowKey key, string effectId, string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task<OAuthFlowRecord> TransitionAsync(OAuthFlowKey key, long expectedRevision, OAuthFlowStatus status, DateTimeOffset now, string? safeReason, CredentialRef? credentialRef, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken);
}

public sealed record BeginOAuthRequest(
    V2RequestContext Context,
    string Provider,
    IReadOnlyList<string> CapabilityIds,
    string RedirectUri,
    TimeSpan Lifetime);

public sealed record BeginOAuthResult(Uri AuthorizationUri, string State, OAuthFlowKey FlowKey, DateTimeOffset ExpiresAt);

public sealed record OAuthCallbackRequest(
    string Provider,
    string State,
    string RedirectUri,
    string? Code,
    string? Error,
    string? ErrorDescription);

public sealed record OAuthCallbackResult(
    OAuthFlowKey FlowKey,
    OAuthFlowStatus Status,
    string? EffectId,
    CredentialRef? CredentialRef,
    bool Duplicate,
    string? SafeReason = null);
