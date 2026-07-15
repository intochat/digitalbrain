using DigitalBrain.Kernel.Contracts;
using Orleans;
namespace DigitalBrain.Kernel.Features;

[GenerateSerializer, Alias("digitalbrain.v3.feature-hub-state")]
internal sealed record FeatureHubState(
    [property: Id(0)] FeatureInstallationRegistration[] Installations,
    [property: Id(1)] long Revision,
    [property: Id(2)] FeatureFanOutState[] FanOuts,
    [property: Id(3)] FeatureReleaseMetadata[] Releases,
    [property: Id(4)] FeatureApprovalState[] Approvals,
    [property: Id(5)] FeatureInstallationAuthorityState[] Authorities,
    [property: Id(6)] FeatureBackpressureAlert[] Alerts,
    [property: Id(7)] FeatureDraft[]? Drafts = null,
    [property: Id(8)] FeatureDraftCommandReplay[]? DraftReplays = null)
{
    public static FeatureHubState Empty { get; } = new([], 0, [], [], [], [], [], [], []);
}
[GenerateSerializer, Alias("digitalbrain.feature.draft-command-replay.v1")]
internal sealed record FeatureDraftCommandReplay(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] string IdempotencyId,
    [property: Id(2)] string Kind,
    [property: Id(3)] string PayloadDigest,
    [property: Id(4)] string ResultStatus,
    [property: Id(5)] FeatureBehavior ResultBehavior,
    [property: Id(6)] FeatureSourceSnapshot ResultSource,
    [property: Id(7)] FeatureVerification? ResultVerification,
    [property: Id(8)] FeatureInstallationId? ResultInstallationId,
    [property: Id(9)] long ResultRevision,
    [property: Id(10)] DateTimeOffset ResultUpdatedAt,
    [property: Id(11)] int Utf8Bytes)
{
    public FeatureDraft Result(FeatureDraft current) => new(
        current.DraftId,
        current.OriginatingRequest,
        current.Goal,
        ResultStatus,
        ResultBehavior,
        ResultSource,
        ResultVerification,
        ResultInstallationId,
        ResultRevision,
        current.CreatedAt,
        ResultUpdatedAt);
}
[GenerateSerializer, Alias("digitalbrain.v3.feature-approval-state")]
internal sealed record FeatureApprovalState(
    [property: Id(0)] string ApprovalId,
    [property: Id(1)] FeatureInstallationId InstallationId,
    [property: Id(2)] FeatureReleaseMetadata Release,
    [property: Id(3)] string[] AddedCapabilities,
    [property: Id(4)] string[] RemovedCapabilities,
    [property: Id(5)] FeatureApprovalStatus Status,
    [property: Id(6)] string? DecisionId,
    [property: Id(7)] DateTimeOffset? DecidedAt,
    [property: Id(8)] long Revision,
    [property: Id(9)] FeatureGrantState[] Grants);
[GenerateSerializer, Alias("digitalbrain.v3.feature-grant-state")]
internal sealed record FeatureGrantState(
    [property: Id(0)] string CapabilityId,
    [property: Id(1)] int CapabilityVersion,
    [property: Id(2)] ProviderConnectionId? ProviderConnectionId,
    [property: Id(3)] string ConstraintsJson,
    [property: Id(4)] string? Provider);
[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-authority-state")]
internal sealed record FeatureInstallationAuthorityState(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ActorId ActorId,
    [property: Id(2)] ReleaseDigest? ActiveRelease,
    [property: Id(3)] ReleaseDigest? PreviousRelease,
    [property: Id(4)] GrantRevision? ActiveGrantRevision,
    [property: Id(5)] FeatureGrantState[] ActiveGrants,
    [property: Id(6)] GrantRevision? PreviousGrantRevision,
    [property: Id(7)] FeatureGrantState[] PreviousGrants,
    [property: Id(8)] ReleaseDigest? PendingRelease,
    [property: Id(9)] GrantRevision? PendingGrantRevision,
    [property: Id(10)] FeatureGrantState[] PendingGrants,
    [property: Id(11)] bool Paused,
    [property: Id(12)] string? PauseReason);
[GenerateSerializer, Alias("digitalbrain.v3.feature-fanout-delivery-state")]
internal sealed record FeatureFanOutDeliveryState([property: Id(0)] FeatureInstallationId InstallationId, [property: Id(1)] bool Delivered);
[GenerateSerializer, Alias("digitalbrain.v3.feature-fanout-state")]
internal sealed record FeatureFanOutState([property: Id(0)] FeatureInput Input, [property: Id(1)] FeatureFanOutDeliveryState[] Deliveries);
[GenerateSerializer, Alias("digitalbrain.v3.feature-inbox-entry")]
internal sealed record FeatureInboxEntry(
    [property: Id(0)] FeatureInput Input,
    [property: Id(1)] int Attempts,
    [property: Id(2)] DateTimeOffset NotBefore,
    [property: Id(3)] bool Parked,
    [property: Id(4)] string? LastFailure);
[GenerateSerializer, Alias("digitalbrain.v3.feature-lease")]
internal sealed record FeatureLease(
    [property: Id(0)] string HostId,
    [property: Id(1)] FeatureLeaseFence Fence,
    [property: Id(2)] DateTimeOffset ExpiresAt,
    [property: Id(3)] int Attempt);
[GenerateSerializer, Alias("digitalbrain.v3.feature-completion")]
internal sealed record FeatureCompletion(
    [property: Id(0)] string InputId,
    [property: Id(1)] long Fence,
    [property: Id(2)] string ResultJson,
    [property: Id(3)] DateTimeOffset CompletedAt,
    [property: Id(4)] string CommitDigest,
    [property: Id(5)] string InputDigest);
[GenerateSerializer, Alias("digitalbrain.v3.persisted-feature-intent")]
internal sealed record PersistedFeatureIntent(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] FeatureIntentKind Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset? AppliedAt);
[GenerateSerializer, Alias("digitalbrain.v3.feature-schedule-cursor")]
internal sealed record FeatureScheduleCursor([property: Id(0)] string ScheduleId, [property: Id(1)] DateTimeOffset LastOccurrenceAt, [property: Id(2)] DateTimeOffset NextOccurrenceAt);
[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-state")]
internal sealed record FeatureInstallationState(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest ActiveRelease,
    [property: Id(2)] ReleaseDigest? PreviousRelease,
    [property: Id(3)] string StateJson,
    [property: Id(4)] bool Paused,
    [property: Id(5)] FeatureInboxEntry[] Inbox,
    [property: Id(6)] FeatureLease? Lease,
    [property: Id(7)] FeatureCompletion[] Completions,
    [property: Id(8)] PersistedFeatureIntent[] Intents,
    [property: Id(9)] long NextFence,
    [property: Id(10)] long Revision,
    [property: Id(11)] string? PauseReason,
    [property: Id(12)] FeatureScheduleCursor[] Schedules)
{
    public static FeatureInstallationState Create(ReleaseDigest release, FeatureInstallationId? installationId = null) =>
        new(installationId ?? new FeatureInstallationId("unbound"), release, null, "{}", false, [], null, [], [], 0, 0, null, []);
}
internal sealed record FeatureCreateDraftTransition(FeatureHubState State, FeatureDraft Draft);
internal sealed record FeatureDraftAuthoringTransition(FeatureHubState State, FeatureDraft Draft);
internal sealed record FeatureAppendTransition(FeatureInstallationState State, FeatureAppendStatus Status);
internal sealed record FeatureClaimTransition(FeatureInstallationState State, FeatureRunClaim? Claim);
internal sealed record FeatureCommitTransition(FeatureInstallationState State, FeatureCompletion Completion);
