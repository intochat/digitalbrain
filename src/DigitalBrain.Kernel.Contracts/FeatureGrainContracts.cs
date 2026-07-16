using System.Text;
using DigitalBrain.Kernel.Runtime;
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
public sealed record FeatureInstallationRegistration([property: Id(0)] FeatureInstallationId InstallationId, [property: Id(1)] ReleaseDigest Release, [property: Id(2)] string[] Subscriptions);
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
    [property: Id(4)] string[] Dependencies,
    [property: Id(5)] FeatureSourceSnapshot? Source = null);
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
    [property: Id(3)] string DecisionId,
    [property: Id(4)] ActorId? ActorId = null);
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
    Rejected,
    Superseded
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
    [property: Id(9)] FeatureGrantSpec[] Grants,
    [property: Id(10)] ActorId? DecisionActorId = null);
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
    [property: Id(10)] string? PauseReason,
    [property: Id(11)] FeatureRollbackReplaySnapshot? RollbackReplay = null,
    [property: Id(12)] bool ExactRollbackAvailable = false,
    [property: Id(13)] bool PublicationConfirmed = false);
[GenerateSerializer, Alias("digitalbrain.v3.feature-rollback-replay-snapshot")]
public sealed record FeatureRollbackReplaySnapshot(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest ExpectedActiveRelease,
    [property: Id(2)] ReleaseDigest TargetRelease,
    [property: Id(3)] long ExpectedRevision,
    [property: Id(4)] string IdempotencyId);
[GenerateSerializer, Alias("digitalbrain.v3.feature-grant-snapshot")]
public sealed record FeatureGrantSnapshot(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest Release,
    [property: Id(2)] FeatureGrantSpec Grant,
    [property: Id(3)] GrantRevision Revision,
    [property: Id(4)] ActorId ActorId,
    [property: Id(5)] bool Paused);
[GenerateSerializer, Alias("digitalbrain.feature.capability-projection.v1")]
public sealed record FeatureCapabilityProjection(
    [property: Id(0)] BrainOwnerId OwnerId,
    [property: Id(1)] FeatureInstallationId InstallationId,
    [property: Id(2)] ActorId ActorId,
    [property: Id(3)] ReleaseDigest Release,
    [property: Id(4)] GrantRevision GrantRevision,
    [property: Id(5)] string Goal,
    [property: Id(6)] FeatureScenario[] Scenarios,
    [property: Id(7)] FeatureGrantSpec[] Grants,
    [property: Id(8)] string InputKind,
    [property: Id(9)] long PublicationFence,
    [property: Id(10)] string AuthorityDigest,
    [property: Id(11)] string AccessDigest);
[GenerateSerializer, Alias("digitalbrain.feature.start-run.v1")]
public sealed record StartFeatureRun(
    [property: Id(0)] BrainOwnerId OwnerId,
    [property: Id(1)] ActorId ActorId,
    [property: Id(2)] FeatureInstallationId InstallationId,
    [property: Id(3)] ReleaseDigest Release,
    [property: Id(4)] GrantRevision GrantRevision,
    [property: Id(5)] long PublicationFence,
    [property: Id(6)] string AuthorityDigest,
    [property: Id(7)] string AccessDigest,
    [property: Id(8)] FeatureInput Input);
[Alias("digitalbrain.feature.run-origin.v1")]
public enum FeatureRunOrigin
{
    Unspecified,
    Chat,
    Direct,
    Schedule,
    Event
}
[GenerateSerializer, Alias("digitalbrain.feature.run-origin-reference.v1")]
public sealed record FeatureRunOriginReference(
    [property: Id(0)] string? ConversationId,
    [property: Id(1)] string? RequestId,
    [property: Id(2)] string? AutomationId);
[Alias("digitalbrain.feature.run-status.v1")]
public enum FeatureRunStatus
{
    Queued,
    Running,
    WaitingForApproval,
    Completed,
    Failed,
    Parked
}
[Alias("digitalbrain.feature.run-authority-state.v1")]
public enum FeatureRunAuthorityState
{
    Authorized,
    WaitingForApproval,
    Paused
}
[GenerateSerializer, Alias("digitalbrain.feature.run-snapshot.v1")]
public sealed record FeatureRunSnapshot(
    [property: Id(0)] string RunId,
    [property: Id(1)] FeatureInstallationId InstallationId,
    [property: Id(2)] ReleaseDigest Release,
    [property: Id(3)] string InputKind,
    [property: Id(4)] FeatureRunOrigin Origin,
    [property: Id(5)] FeatureRunOriginReference? OriginReference,
    [property: Id(6)] FeatureRunStatus Status,
    [property: Id(7)] FeatureRunAuthorityState AuthorityState,
    [property: Id(8)] DateTimeOffset OccurredAt,
    [property: Id(9)] DateTimeOffset? StartedAt,
    [property: Id(10)] DateTimeOffset? CompletedAt,
    [property: Id(11)] DateTimeOffset? RetryAt,
    [property: Id(12)] int Attempts,
    [property: Id(13)] string? ResultSurfaceReference,
    [property: Id(14)] string? SafeFailure,
    [property: Id(15)] string? FailureGuidance,
    [property: Id(16)] string TraceReference);
[GenerateSerializer, Alias("digitalbrain.feature.run-read-request.v1")]
public sealed record FeatureRunReadRequest(
    [property: Id(0)] int Limit,
    [property: Id(1)] FeatureRunStatus? Status = null,
    [property: Id(2)] FeatureRunOrigin? Origin = null,
    [property: Id(3)] string? RunId = null)
{
    public const int MaximumLimit = 200;
}
[GenerateSerializer, Alias("digitalbrain.feature.run-collection-snapshot.v1")]
public sealed record FeatureRunCollectionSnapshot(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest ActiveRelease,
    [property: Id(2)] long Revision,
    [property: Id(3)] FeatureRunSnapshot[] Runs);
[GenerateSerializer, Alias("digitalbrain.v3.feature-input")]
public sealed record FeatureInput(
    [property: Id(0)] string InputId,
    [property: Id(1)] string Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset OccurredAt,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] string TraceId,
    [property: Id(6)] string? CausationId = null,
    [property: Id(7)] FeatureRunOrigin Origin = FeatureRunOrigin.Unspecified,
    [property: Id(8)] FeatureRunOriginReference? OriginReference = null);
[GenerateSerializer, Alias("digitalbrain.v3.feature-lease-fence")]
public sealed record FeatureLeaseFence([property: Id(0)] string InputId, [property: Id(1)] long Fence);
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
public sealed record FeatureIntent([property: Id(0)] string LogicalOperationKey, [property: Id(1)] FeatureIntentKind Kind, [property: Id(2)] string PayloadJson);
public static class FeatureIntentKeys
{
    public static string Create(FeatureInstallationId installationId, string inputId, string logicalOperationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalOperationKey);
        return $"{installationId.Value.Length}:{installationId.Value}{inputId.Length}:{inputId}{logicalOperationKey.Length}:{logicalOperationKey}";
    }
}
[GenerateSerializer, Alias("digitalbrain.v3.feature-resource-usage")]
public sealed record FeatureResourceUsage([property: Id(0)] int Reads, [property: Id(1)] int ModelCalls);
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
[GenerateSerializer, Alias("digitalbrain.feature.effect-resolution.v1")]
public sealed record FeatureEffectResolution(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] string DecisionId,
    [property: Id(2)] string ActorScope,
    [property: Id(3)] InoEffectTerminalKind TerminalKind,
    [property: Id(4)] DateTimeOffset ResolvedAt,
    [property: Id(5)] string SafeResult);
[GenerateSerializer, Alias("digitalbrain.v3.feature-intent-status")]
public sealed record FeatureIntentStatus(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] FeatureIntentKind Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset? AppliedAt,
    [property: Id(4)] string? InputId = null,
    [property: Id(5)] DateTimeOffset? DeclinedAt = null,
    [property: Id(6)] string? PayloadDigest = null,
    [property: Id(7)] FeatureEffectResolution? Resolution = null);
[GenerateSerializer, Alias("digitalbrain.v3.feature-schedule-status")]
public sealed record FeatureScheduleStatus([property: Id(0)] string ScheduleId, [property: Id(1)] DateTimeOffset LastOccurrenceAt, [property: Id(2)] DateTimeOffset NextOccurrenceAt);
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
    [property: Id(12)] FeatureParkedInput[] Parked,
    [property: Id(13)] FeatureReleaseSwitchSnapshot? UnconfirmedReleaseSwitch = null,
    [property: Id(14)] FeatureRunSnapshot[]? Runs = null);
[GenerateSerializer, Alias("digitalbrain.feature.release-switch-snapshot.v1")]
public sealed record FeatureReleaseSwitchSnapshot(
    [property: Id(0)] string OperationToken,
    [property: Id(1)] ReleaseDigest FromActiveRelease,
    [property: Id(2)] ReleaseDigest? FromPreviousRelease,
    [property: Id(3)] ReleaseDigest ToRelease,
    [property: Id(4)] long FromRevision,
    [property: Id(5)] long SwitchRevision);
[GenerateSerializer, Alias("digitalbrain.feature.runtime-reservation.v1")]
public sealed record FeatureRuntimeReservation(
    [property: Id(0)] BrainOwnerId OwnerId,
    [property: Id(1)] FeatureDraftId DraftId,
    [property: Id(2)] FeatureInstallationId InstallationId,
    [property: Id(3)] ActorId ActorId,
    [property: Id(4)] string ReservationToken,
    [property: Id(5)] string AccessDigest,
    [property: Id(6)] ReleaseDigest CandidateRelease,
    [property: Id(7)] long? RuntimeRevision,
    [property: Id(8)] ReleaseDigest? RuntimeActiveRelease,
    [property: Id(9)] ReleaseDigest? RuntimePreviousRelease,
    [property: Id(10)] bool? RuntimePaused,
    [property: Id(11)] string? RuntimePauseReason);
[Alias("digitalbrain.feature.runtime-reservation-phase.v1")]
public enum FeatureRuntimeReservationPhase
{
    Reserved,
    Switched,
    Resetting
}
[GenerateSerializer, Alias("digitalbrain.feature.runtime-reservation-snapshot.v1")]
public sealed record FeatureRuntimeReservationSnapshot(
    [property: Id(0)] FeatureRuntimeReservation Reservation,
    [property: Id(1)] FeatureRuntimeReservationPhase Phase,
    [property: Id(2)] bool RuntimeExists,
    [property: Id(3)] long? ObservedRevision,
    [property: Id(4)] ReleaseDigest? ObservedActiveRelease,
    [property: Id(5)] ReleaseDigest? ObservedPreviousRelease,
    [property: Id(6)] bool? ObservedPaused,
    [property: Id(7)] string? ObservedPauseReason,
    [property: Id(8)] FeatureLeaseStatus? ObservedLease);
[GenerateSerializer, Alias("digitalbrain.feature.runtime-reservation-release.v1")]
public sealed record FeatureRuntimeReservationRelease(
    [property: Id(0)] FeatureRuntimeReservation Reservation,
    [property: Id(1)] FeatureRuntimeReservationPhase ExpectedPhase,
    [property: Id(2)] ReleaseDigest? ExpectedActiveRelease,
    [property: Id(3)] ReleaseDigest? ExpectedPreviousRelease,
    [property: Id(4)] bool RequireRuntimeAbsent);
[GenerateSerializer, Alias("digitalbrain.v3.feature-parked-input")]
public sealed record FeatureParkedInput([property: Id(0)] FeatureInput Input, [property: Id(1)] int Attempts, [property: Id(2)] string? SafeFailure);
[Alias("digitalbrain.v3.feature-failure-disposition")]
public enum FeatureFailureDisposition
{
    RetryScheduled,
    Parked
}
[GenerateSerializer, Alias("digitalbrain.v3.feature-fanout-result")]
public sealed record FeatureFanOutResult([property: Id(0)] string InputId, [property: Id(1)] int Delivered, [property: Id(2)] int Pending);
[GenerateSerializer, Alias("digitalbrain.v3.feature-backpressure-alert")]
public sealed record FeatureBackpressureAlert(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] string InputId,
    [property: Id(2)] string Kind,
    [property: Id(3)] DateTimeOffset OccurredAt,
    [property: Id(4)] string Reason);
[GenerateSerializer, Alias("digitalbrain.v3.feature-hub-snapshot")]
public sealed record FeatureHubSnapshot(
    [property: Id(0)] FeatureInstallationRegistration[] Installations,
    [property: Id(1)] FeatureFanOutResult[] FanOuts,
    [property: Id(2)] long Revision,
    [property: Id(3)] FeatureReleaseMetadata[] Releases,
    [property: Id(4)] FeatureApprovalSnapshot[] Approvals,
    [property: Id(5)] FeatureAuthoritySnapshot[] Authorities,
    [property: Id(6)] FeatureBackpressureAlert[] Alerts);
[GenerateSerializer, Alias("digitalbrain.feature.draft-proposal.v1")]
public sealed record FeatureDraft
{
    internal const string LegacyMissingConversationId = "\0digitalbrain-legacy-missing";

    [Id(0)] private readonly string _draftIdentifier = string.Empty;
    [Id(1)] private readonly string _operationIdentifier = string.Empty;
    [Id(2)] private readonly string _goal = string.Empty;
    [Id(3)] private readonly string _status = string.Empty;
    [Id(4)] private readonly DateTimeOffset _createdAt;
    [Id(5)] private readonly OriginatingRequest? _originatingRequest;
    [Id(6)] private readonly FeatureBehavior? _behavior;
    [Id(7)] private readonly FeatureSourceSnapshot? _source;
    [Id(8)] private readonly FeatureVerification? _verification;
    [Id(9)] private readonly FeatureInstallationId? _installationId;
    [Id(10)] private readonly long _revision;
    [Id(11)] private readonly DateTimeOffset _updatedAt;

    public FeatureDraft(
        FeatureDraftId draftId,
        OriginatingRequest originatingRequest,
        string goal,
        string status,
        FeatureBehavior behavior,
        FeatureSourceSnapshot source,
        FeatureVerification? verification,
        FeatureInstallationId? installationId,
        long revision,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        _draftIdentifier = draftId.Value;
        _operationIdentifier = originatingRequest.OperationId;
        _goal = goal;
        _status = status;
        _createdAt = createdAt;
        _originatingRequest = originatingRequest;
        _behavior = behavior;
        _source = source;
        _verification = verification;
        _installationId = installationId;
        _revision = revision;
        _updatedAt = updatedAt;
    }

    public FeatureDraftId DraftId => new(_draftIdentifier);
    public OriginatingRequest OriginatingRequest => _originatingRequest ?? new(_operationIdentifier, LegacyMissingConversationId, _goal);
    public string Goal => _goal;
    public string Status => _status;
    public FeatureBehavior Behavior => _behavior ?? SeedBehavior();
    public FeatureSourceSnapshot Source => _source ?? SeedSource();
    public FeatureVerification? Verification => _verification;
    public FeatureInstallationId? InstallationId => _installationId;
    public long Revision => _revision;
    public DateTimeOffset CreatedAt => _createdAt;
    public DateTimeOffset UpdatedAt => _updatedAt == default ? _createdAt : _updatedAt;

    internal static FeatureDraft RestoreLegacy(
        string proposalId,
        string operationId,
        string goal,
        string status,
        DateTimeOffset createdAt) => new(
            new FeatureDraftId(proposalId),
            new OriginatingRequest(operationId, LegacyMissingConversationId, goal),
            goal,
            status,
            SeedBehavior(),
            SeedSource(),
            null,
            null,
            0,
            createdAt,
            createdAt);

    private static FeatureBehavior SeedBehavior() => new([
        new FeatureScenario(
            "scenario-1",
            "Describe the intended outcome",
            "the Feature Draft is editable",
            "the Behavior is revised",
            "the intended outcome is recorded")
    ]);

    private static FeatureSourceSnapshot SeedSource()
    {
        const string implementationProject = "src/RuntimeAuthoredFeature/RuntimeAuthoredFeature.csproj";
        const string scenarioProject = "tests/RuntimeAuthoredFeature.Scenarios/RuntimeAuthoredFeature.Scenarios.csproj";
        return new FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            [
                new FeatureSourceFile(implementationProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                new FeatureSourceFile(scenarioProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
            ]);
    }
}
[GenerateSerializer, Alias("digitalbrain.feature.draft-id.v1")]
public sealed record FeatureDraftId([property: Id(0)] string Value);
[GenerateSerializer, Alias("digitalbrain.feature.originating-request.v1")]
public sealed record OriginatingRequest(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string ConversationId,
    [property: Id(2)] string Text);
[GenerateSerializer, Alias("digitalbrain.feature.scenario.v1")]
public sealed record FeatureScenario(
    [property: Id(0)] string ScenarioId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Given,
    [property: Id(3)] string When,
    [property: Id(4)] string Then);
[GenerateSerializer, Alias("digitalbrain.feature.behavior.v1")]
public sealed record FeatureBehavior([property: Id(0)] FeatureScenario[] Scenarios);
[GenerateSerializer, Alias("digitalbrain.feature.source-file.v1")]
public sealed record FeatureSourceFile(
    [property: Id(0)] string Path,
    [property: Id(1)] string Content);
[GenerateSerializer, Alias("digitalbrain.feature.source-snapshot.v1")]
public sealed record FeatureSourceSnapshot(
    [property: Id(0)] string ImplementationProjectPath,
    [property: Id(1)] string ScenarioProjectPath,
    [property: Id(2)] FeatureSourceFile[] Files);
[GenerateSerializer, Alias("digitalbrain.feature.verification.v1")]
public sealed record FeatureVerification(
    [property: Id(0)] ReleaseDigest Release,
    [property: Id(1)] int Total,
    [property: Id(2)] int Passed,
    [property: Id(3)] int Failed,
    [property: Id(4)] int Skipped,
    [property: Id(5)] DateTimeOffset VerifiedAt,
    [property: Id(6)] FeatureVerificationEvidence? Evidence = null);
[GenerateSerializer, Alias("digitalbrain.feature.create-draft.v1")]
public sealed record CreateFeatureDraft(
    [property: Id(0)] string OperationId,
    [property: Id(1)] string Goal,
    [property: Id(2)] DateTimeOffset RequestedAt,
    [property: Id(3)] string ConversationId);
[GenerateSerializer, Alias("digitalbrain.feature.revise-behavior.v1")]
public sealed record ReviseFeatureBehavior(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] FeatureBehavior Behavior,
    [property: Id(2)] long ExpectedRevision,
    [property: Id(3)] string IdempotencyId,
    [property: Id(4)] DateTimeOffset RevisedAt);
[GenerateSerializer, Alias("digitalbrain.feature.revise-source.v1")]
public sealed record ReviseFeatureSource(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] FeatureSourceSnapshot Source,
    [property: Id(2)] long ExpectedRevision,
    [property: Id(3)] string IdempotencyId,
    [property: Id(4)] DateTimeOffset RevisedAt);
[GenerateSerializer, Alias("digitalbrain.feature.record-verification.v1")]
public sealed record RecordFeatureVerification(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] FeatureVerification Verification,
    [property: Id(2)] long ExpectedRevision,
    [property: Id(3)] string IdempotencyId);
[GenerateSerializer, Alias("digitalbrain.feature.mark-installed.v1")]
public sealed record MarkFeatureDraftInstalled(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] FeatureInstallationId InstallationId,
    [property: Id(2)] ReleaseDigest Release,
    [property: Id(3)] long ExpectedRevision,
    [property: Id(4)] string IdempotencyId,
    [property: Id(5)] DateTimeOffset InstalledAt);
[GenerateSerializer, Alias("digitalbrain.feature.rollback-installation.v1")]
public sealed record RollbackFeatureInstallation(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest ExpectedActiveRelease,
    [property: Id(2)] ReleaseDigest TargetRelease,
    [property: Id(3)] long ExpectedRevision,
    [property: Id(4)] string IdempotencyId);
public interface IFeatureGrainResolver
{
    IFeatureHubGrain Hub(BrainOwnerId ownerId);
    IFeatureInstallationGrain Installation(BrainOwnerId ownerId, FeatureInstallationId installationId);
}
[Alias("digitalbrain.v3.feature-hub-grain")]
public interface IFeatureHubGrain : IGrainWithStringKey
{
    [Alias("register")]
    Task RegisterAsync(FeatureInstallationRegistration registration);
    [Alias("create-draft")]
    Task<FeatureDraft> CreateDraftAsync(CreateFeatureDraft request);
    [Alias("read-draft")]
    Task<FeatureDraft?> ReadDraftAsync(FeatureDraftId draftId);
    [Alias("read-installed-draft")]
    Task<FeatureDraft?> ReadInstalledDraftAsync(FeatureInstallationId installationId, ReleaseDigest release);
    [Alias("read-capability-catalog")]
    Task<FeatureCapabilityProjection[]> ReadCapabilityCatalogAsync(ActorId actorId) =>
        Task.FromResult(Array.Empty<FeatureCapabilityProjection>());
    [Alias("start-feature-run")]
    Task<FeatureAppendStatus> StartFeatureRunAsync(StartFeatureRun command) =>
        Task.FromException<FeatureAppendStatus>(
            new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
    [Alias("revise-behavior")]
    Task<FeatureDraft> ReviseBehaviorAsync(ReviseFeatureBehavior command);
    [Alias("revise-source")]
    Task<FeatureDraft> ReviseSourceAsync(ReviseFeatureSource command);
    [Alias("accept-suggested-change")]
    Task<FeatureDraft> AcceptSuggestedChangeAsync(AcceptSuggestedChange command);
    [Alias("reject-suggested-change")]
    Task<FeatureDraft> RejectSuggestedChangeAsync(RejectSuggestedChange command);
    [Alias("record-verification")]
    Task<FeatureDraft> RecordVerificationAsync(RecordFeatureVerification command);
    [Alias("acquire-draft-installation-reservation")]
    Task<FeatureDraftInstallationReservation> AcquireDraftInstallationReservationAsync(InstallFeatureVersion command, ActorId actorId);
    [Alias("read-draft-installation-reservation")]
    Task<FeatureDraftInstallationReservation?> ReadDraftInstallationReservationAsync(FeatureDraftId draftId);
    [Alias("read-draft-installation-reset")]
    Task<FeatureDraftInstallationResetObligation?> ReadDraftInstallationResetAsync(FeatureDraftId draftId);
    [Alias("reset-draft-installation-reservation")]
    Task<FeatureDraftInstallationResetPreparation> ResetDraftInstallationReservationAsync(ResetFeatureDraftInstallationReservation command, ActorId actorId);
    [Alias("complete-draft-installation-reservation-reset")]
    Task<FeatureDraft> CompleteDraftInstallationReservationResetAsync(FeatureDraftId draftId, string idempotencyId, ActorId actorId);
    [Alias("prepare-active-publication")]
    Task<FeaturePublicationTicket> PrepareActivePublicationAsync(FeatureInstallationId installationId);
    [Alias("confirm-active-publication")]
    Task<FeaturePublicationReceipt> ConfirmActivePublicationAsync(FeaturePublicationReceipt receipt);
    [Alias("mark-draft-installed")]
    Task<FeatureDraft> MarkDraftInstalledAsync(MarkFeatureDraftInstalled command);
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
    [Alias("rollback-installation-exact")]
    Task<FeatureAuthoritySnapshot> RollbackInstallationExactAsync(RollbackFeatureInstallation command) =>
        Task.FromException<FeatureAuthoritySnapshot>(
            new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
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
    [Alias("append-exact")]
    Task<FeatureAppendStatus> AppendExactAsync(ReleaseDigest expectedRelease, FeatureInput input) =>
        Task.FromException<FeatureAppendStatus>(
            new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
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
    Task ApplyIntentAsync(string operationKey) =>
        Task.FromException(new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
    [Alias("decline-intent")]
    Task DeclineIntentAsync(string operationKey) =>
        Task.FromException(new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
    [Alias("resolve-intent")]
    Task ResolveIntentAsync(FeatureEffectResolution resolution) =>
        Task.FromException(new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
    [Alias("pause")]
    Task PauseAsync(string reason);
    [Alias("resume")]
    Task ResumeAsync();
    [Alias("switch-release")]
    Task SwitchReleaseAsync(ReleaseDigest release);
    [Alias("establish-reservation")]
    Task<FeatureRuntimeReservationSnapshot> EstablishReservationAsync(FeatureRuntimeReservation reservation);
    [Alias("read-reservation")]
    Task<FeatureRuntimeReservationSnapshot?> ReadReservationAsync();
    [Alias("activate-reserved-release")]
    Task ActivateReservedReleaseAsync(FeatureRuntimeReservation reservation);
    [Alias("reset-reserved-release")]
    Task ResetReservedReleaseAsync(FeatureRuntimeReservation reservation, bool requireRuntimeAbsence);
    [Alias("release-reservation")]
    Task ReleaseReservationAsync(FeatureRuntimeReservationRelease release);
    [Alias("begin-release-switch")]
    Task BeginReleaseSwitchAsync(ReleaseDigest release, string operationToken);
    [Alias("confirm-release-switch")]
    Task ConfirmReleaseSwitchAsync(ReleaseDigest release);
    [Alias("clear-backpressure-pause")]
    Task ClearBackpressurePauseAsync();
    [Alias("discard-unpublished")]
    Task DiscardUnpublishedAsync(ReleaseDigest release, bool requireAbsent);
    [Alias("restore-unpublished-candidate")]
    Task RestoreUnpublishedCandidateAsync(
        ReleaseDigest candidateRelease,
        ReleaseDigest expectedActiveRelease,
        ReleaseDigest? expectedPreviousRelease,
        long minimumFromRevision);
    [Alias("rollback")]
    Task RollbackAsync();
    [Alias("read")]
    Task<FeatureInstallationSnapshot> ReadAsync();
    [Alias("read-runs")]
    Task<FeatureRunCollectionSnapshot> ReadRunsAsync(FeatureRunReadRequest request) =>
        Task.FromException<FeatureRunCollectionSnapshot>(
            new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable));
}
