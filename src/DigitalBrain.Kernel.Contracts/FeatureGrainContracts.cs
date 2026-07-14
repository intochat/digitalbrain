using System.Text;
using Orleans;

namespace DigitalBrain.Kernel.Contracts;

public static class FeatureGrainIds
{
    public static string Hub(BrainOwnerId ownerId) => $"v3/{Segment(ownerId.Value)}/features";

    public static string Installation(BrainOwnerId ownerId, FeatureInstallationId installationId) =>
        $"{Hub(ownerId)}/{Segment(installationId.Value)}";

    public static BrainOwnerId ParseHub(string grainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainKey);
        var segments = grainKey.Split('/', StringSplitOptions.None);
        if (segments is not ["v3", var owner, "features"])
            throw new ArgumentException("A canonical feature hub key is required.", nameof(grainKey));
        var ownerId = new BrainOwnerId(Unsegment(owner));
        if (!string.Equals(Hub(ownerId), grainKey, StringComparison.Ordinal))
            throw new ArgumentException("A canonical feature hub key is required.", nameof(grainKey));
        return ownerId;
    }

    public static (BrainOwnerId OwnerId, FeatureInstallationId InstallationId) ParseInstallation(string grainKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainKey);
        var segments = grainKey.Split('/', StringSplitOptions.None);
        if (segments is not ["v3", var owner, "features", var installation])
            throw new ArgumentException("A canonical feature installation key is required.", nameof(grainKey));
        var ownerId = new BrainOwnerId(Unsegment(owner));
        var installationId = new FeatureInstallationId(Unsegment(installation));
        if (!string.Equals(Installation(ownerId, installationId), grainKey, StringComparison.Ordinal))
            throw new ArgumentException("A canonical feature installation key is required.", nameof(grainKey));
        return (ownerId, installationId);
    }

    private static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Unsegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("A canonical feature grain key is required.", nameof(value), exception);
        }
    }
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-registration")]
public sealed record FeatureInstallationRegistration(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] string[] Subscriptions);

[Alias("digitalbrain.v3.feature-source-kind")]
public enum FeatureSourceKind
{
    Repository,
    RuntimeAuthored
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-release-metadata")]
public sealed record FeatureReleaseMetadata(
    [property: Id(0)] ReleaseDigest Digest,
    [property: Id(1)] string SourceReference,
    [property: Id(2)] FeatureSourceKind SourceKind,
    [property: Id(3)] string[] RequestedCapabilities,
    [property: Id(4)] string[] Dependencies);

[GenerateSerializer, Alias("digitalbrain.v3.feature-release-proposal")]
public sealed record FeatureReleaseProposal(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] FeatureReleaseMetadata Release,
    [property: Id(2)] FeatureGrantSpec[] Grants);

[GenerateSerializer, Alias("digitalbrain.v3.feature-grant-spec")]
public sealed record FeatureGrantSpec(
    [property: Id(0)] string CapabilityId,
    [property: Id(1)] int CapabilityVersion,
    [property: Id(2)] ProviderConnectionId? ProviderConnectionId,
    [property: Id(3)] string ConstraintsJson,
    [property: Id(4)] string? Provider = null);

[GenerateSerializer, Alias("digitalbrain.v3.feature-approval-decision")]
public sealed record FeatureApprovalDecision(
    [property: Id(0)] string ApprovalId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] bool Approved,
    [property: Id(3)] string DecisionId);

[GenerateSerializer, Alias("digitalbrain.v3.feature-grant-request")]
public sealed record FeatureGrantRequest(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] ActorId ActorId,
    [property: Id(3)] FeatureGrantSpec[] Grants);

[GenerateSerializer, Alias("digitalbrain.v3.feature-grant-revocation")]
public sealed record FeatureGrantRevocation(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] string CapabilityId,
    [property: Id(3)] int CapabilityVersion);

[GenerateSerializer, Alias("digitalbrain.v3.feature-grant-lookup")]
public sealed record FeatureGrantLookup(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] string CapabilityId,
    [property: Id(3)] int CapabilityVersion);

[Alias("digitalbrain.v3.feature-approval-status")]
public enum FeatureApprovalStatus
{
    Pending,
    Approved,
    Rejected
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-approval-snapshot")]
public sealed record FeatureApprovalSnapshot(
    [property: Id(0)] string ApprovalId,
    [property: Id(1)] FeatureInstallationId InstallationId,
    [property: Id(2)] FeatureReleaseMetadata Release,
    [property: Id(3)] string[] AddedCapabilities,
    [property: Id(4)] string[] RemovedCapabilities,
    [property: Id(5)] FeatureApprovalStatus Status,
    [property: Id(6)] string? DecisionId,
    [property: Id(7)] DateTimeOffset? DecidedAt,
    [property: Id(8)] long Revision,
    [property: Id(9)] FeatureGrantSpec[] Grants);

[GenerateSerializer, Alias("digitalbrain.v3.feature-authority-snapshot")]
public sealed record FeatureAuthoritySnapshot(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ActorId ActorId,
    [property: Id(2)] ReleaseDigest? ActiveRelease,
    [property: Id(3)] ReleaseDigest? PreviousRelease,
    [property: Id(4)] GrantRevision? ActiveGrantRevision,
    [property: Id(5)] FeatureGrantSpec[] ActiveGrants,
    [property: Id(6)] ReleaseDigest? PendingRelease,
    [property: Id(7)] GrantRevision? PendingGrantRevision,
    [property: Id(8)] FeatureGrantSpec[] PendingGrants,
    [property: Id(9)] bool Paused,
    [property: Id(10)] string? PauseReason);

[GenerateSerializer, Alias("digitalbrain.v3.feature-grant-snapshot")]
public sealed record FeatureGrantSnapshot(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] FeatureGrantSpec Grant,
    [property: Id(3)] GrantRevision Revision,
    [property: Id(4)] ActorId ActorId,
    [property: Id(5)] bool Paused);

[GenerateSerializer, Alias("digitalbrain.v3.feature-input")]
public sealed record FeatureInput(
    [property: Id(0)] string InputId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset OccurredAt,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] string TraceId);

[GenerateSerializer, Alias("digitalbrain.v3.feature-lease-fence")]
public sealed record FeatureLeaseFence(
    [property: Id(0)] string InputId,
    [property: Id(1)] long Fence);

[GenerateSerializer, Alias("digitalbrain.v3.feature-run-claim")]
public sealed record FeatureRunClaim(
    [property: Id(0)] FeatureInput Input,
    [property: Id(1)] FeatureLeaseFence Fence,
    [property: Id(2)] ReleaseDigest Release,
    [property: Id(3)] string StateJson,
    [property: Id(4)] DateTimeOffset LeaseExpiresAt,
    [property: Id(5)] int Attempt);

[Alias("digitalbrain.v3.feature-intent-kind")]
public enum FeatureIntentKind
{
    TextSurface,
    Event,
    ExternalEffect,
    InternalWrite
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-intent")]
public sealed record FeatureIntent(
    [property: Id(0)] string LogicalOperationKey,
    [property: Id(1)] FeatureIntentKind Kind,
    [property: Id(2)] string PayloadJson);

[GenerateSerializer, Alias("digitalbrain.v3.feature-resource-usage")]
public sealed record FeatureResourceUsage(
    [property: Id(0)] int Reads,
    [property: Id(1)] int ModelCalls);

[GenerateSerializer, Alias("digitalbrain.v3.feature-run-commit")]
public sealed record FeatureRunCommit(
    [property: Id(0)] FeatureLeaseFence Fence,
    [property: Id(1)] string NewStateJson,
    [property: Id(2)] IReadOnlyList<FeatureIntent> Intents,
    [property: Id(3)] FeatureResourceUsage Usage,
    [property: Id(4)] string ResultJson);

[GenerateSerializer, Alias("digitalbrain.v3.feature-schedule-occurrence")]
public sealed record FeatureScheduleOccurrence(
    [property: Id(0)] string ScheduleId,
    [property: Id(1)] DateTimeOffset ScheduledFor,
    [property: Id(2)] DateTimeOffset NextOccurrenceAt,
    [property: Id(3)] string PayloadJson,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] string TraceId);

[Alias("digitalbrain.v3.feature-append-status")]
public enum FeatureAppendStatus
{
    Accepted,
    Duplicate,
    Full,
    Paused
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-completion-receipt")]
public sealed record FeatureCompletionReceipt(
    [property: Id(0)] string InputId,
    [property: Id(1)] long Fence,
    [property: Id(2)] string ResultJson,
    [property: Id(3)] DateTimeOffset CompletedAt,
    [property: Id(4)] string CommitDigest,
    [property: Id(5)] string InputDigest);

[GenerateSerializer, Alias("digitalbrain.v3.feature-intent-status")]
public sealed record FeatureIntentStatus(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] FeatureIntentKind Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset? AppliedAt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-schedule-status")]
public sealed record FeatureScheduleStatus(
    [property: Id(0)] string ScheduleId,
    [property: Id(1)] DateTimeOffset LastOccurrenceAt,
    [property: Id(2)] DateTimeOffset NextOccurrenceAt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-lease-status")]
public sealed record FeatureLeaseStatus(
    [property: Id(0)] string HostId,
    [property: Id(1)] FeatureLeaseFence Fence,
    [property: Id(2)] DateTimeOffset ExpiresAt,
    [property: Id(3)] int Attempt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-snapshot")]
public sealed record FeatureInstallationSnapshot(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest ActiveRelease,
    [property: Id(2)] ReleaseDigest? PreviousRelease,
    [property: Id(3)] string StateJson,
    [property: Id(4)] bool Paused,
    [property: Id(5)] string? PauseReason,
    [property: Id(6)] FeatureInput[] Inbox,
    [property: Id(7)] FeatureLeaseStatus? Lease,
    [property: Id(8)] FeatureCompletionReceipt[] Completions,
    [property: Id(9)] FeatureIntentStatus[] Intents,
    [property: Id(10)] FeatureScheduleStatus[] Schedules,
    [property: Id(11)] long Revision,
    [property: Id(12)] FeatureParkedInput[] Parked);

[GenerateSerializer, Alias("digitalbrain.v3.feature-parked-input")]
public sealed record FeatureParkedInput(
    [property: Id(0)] FeatureInput Input,
    [property: Id(1)] int Attempts,
    [property: Id(2)] string? SafeFailure);

[Alias("digitalbrain.v3.feature-failure-disposition")]
public enum FeatureFailureDisposition
{
    RetryScheduled,
    Parked
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-fanout-result")]
public sealed record FeatureFanOutResult(
    [property: Id(0)] string InputId,
    [property: Id(1)] int Delivered,
    [property: Id(2)] int Pending);

[GenerateSerializer, Alias("digitalbrain.v3.feature-hub-snapshot")]
public sealed record FeatureHubSnapshot(
    [property: Id(0)] FeatureInstallationRegistration[] Installations,
    [property: Id(1)] FeatureFanOutResult[] FanOuts,
    [property: Id(2)] long Revision,
    [property: Id(3)] FeatureReleaseMetadata[] Releases,
    [property: Id(4)] FeatureApprovalSnapshot[] Approvals,
    [property: Id(5)] FeatureAuthoritySnapshot[] Authorities);

[Alias("digitalbrain.v3.feature-hub-grain")]
public interface IFeatureHubGrain : IGrainWithStringKey
{
    [Alias("register")]
    Task RegisterAsync(FeatureInstallationRegistration registration);
    [Alias("publish")]
    Task<FeatureFanOutResult> PublishAsync(FeatureInput input);
    [Alias("read")]
    Task<FeatureHubSnapshot> ReadAsync();
    [Alias("propose-release")]
    Task<FeatureApprovalSnapshot> ProposeAsync(FeatureReleaseProposal proposal, long expectedRevision);
    [Alias("decide-release")]
    Task<FeatureApprovalSnapshot> DecideAsync(FeatureApprovalDecision decision, long expectedRevision);
    [Alias("grant-release")]
    Task<FeatureAuthoritySnapshot> GrantAsync(FeatureGrantRequest request, long expectedRevision);
    [Alias("install-release")]
    Task<FeatureAuthoritySnapshot> InstallAsync(FeatureInstallationRegistration registration, long expectedRevision);
    [Alias("revoke-grant")]
    Task RevokeAsync(FeatureGrantRevocation revocation, long expectedRevision);
    [Alias("pause-installation")]
    Task PauseInstallationAsync(FeatureInstallationId installationId, string reason, long expectedRevision);
    [Alias("resume-installation")]
    Task ResumeInstallationAsync(FeatureInstallationId installationId, long expectedRevision);
    [Alias("rollback-installation")]
    Task<FeatureAuthoritySnapshot> RollbackInstallationAsync(FeatureInstallationId installationId, long expectedRevision);
    [Alias("read-grant")]
    Task<FeatureGrantSnapshot?> ReadGrantAsync(FeatureGrantLookup lookup);
}

[Alias("digitalbrain.v3.feature-installation-grain")]
public interface IFeatureInstallationGrain : IGrainWithStringKey
{
    [Alias("initialize")]
    Task InitializeAsync(ReleaseDigest release);
    [Alias("append")]
    Task<FeatureAppendStatus> AppendAsync(FeatureInput input);
    [Alias("claim")]
    Task<FeatureRunClaim?> ClaimAsync(string hostId, TimeSpan leaseDuration);
    [Alias("fail")]
    Task<FeatureFailureDisposition> FailAsync(FeatureLeaseFence fence, DateTimeOffset retryAt, string safeFailure);
    [Alias("record-schedule-occurrence")]
    Task<FeatureAppendStatus> RecordScheduleOccurrenceAsync(FeatureScheduleOccurrence occurrence);
    [Alias("commit")]
    Task<FeatureCompletionReceipt> CommitAsync(FeatureRunCommit commit);
    [Alias("list-pending-intents")]
    Task<FeatureIntentStatus[]> ListPendingIntentsAsync();
    [Alias("apply-intent")]
    Task ApplyIntentAsync(string operationKey);
    [Alias("pause")]
    Task PauseAsync(string reason);
    [Alias("resume")]
    Task ResumeAsync();
    [Alias("switch-release")]
    Task SwitchReleaseAsync(ReleaseDigest release);
    [Alias("rollback")]
    Task RollbackAsync();
    [Alias("read")]
    Task<FeatureInstallationSnapshot> ReadAsync();
}
