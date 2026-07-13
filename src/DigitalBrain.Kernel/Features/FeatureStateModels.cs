using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.Kernel.Features;

[GenerateSerializer, Alias("digitalbrain.v3.feature-hub-state")]
public sealed record FeatureHubState(
    [property: Id(0)] FeatureInstallationRegistration[] Installations,
    [property: Id(1)] long Revision,
    [property: Id(2)] FeatureFanOutState[] FanOuts)
{
    public static FeatureHubState Empty { get; } = new([], 0, []);
}

[GenerateSerializer, Alias("digitalbrain.v3.feature-fanout-delivery-state")]
public sealed record FeatureFanOutDeliveryState(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] bool Delivered);

[GenerateSerializer, Alias("digitalbrain.v3.feature-fanout-state")]
public sealed record FeatureFanOutState(
    [property: Id(0)] FeatureInput Input,
    [property: Id(1)] FeatureFanOutDeliveryState[] Deliveries);

[GenerateSerializer, Alias("digitalbrain.v3.feature-inbox-entry")]
public sealed record FeatureInboxEntry(
    [property: Id(0)] FeatureInput Input,
    [property: Id(1)] int Attempts,
    [property: Id(2)] DateTimeOffset NotBefore,
    [property: Id(3)] bool Parked,
    [property: Id(4)] string? LastFailure);

[GenerateSerializer, Alias("digitalbrain.v3.feature-lease")]
public sealed record FeatureLease(
    [property: Id(0)] string HostId,
    [property: Id(1)] FeatureLeaseFence Fence,
    [property: Id(2)] DateTimeOffset ExpiresAt,
    [property: Id(3)] int Attempt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-completion")]
public sealed record FeatureCompletion(
    [property: Id(0)] string InputId,
    [property: Id(1)] long Fence,
    [property: Id(2)] string ResultJson,
    [property: Id(3)] DateTimeOffset CompletedAt,
    [property: Id(4)] string CommitDigest,
    [property: Id(5)] string InputDigest);

[GenerateSerializer, Alias("digitalbrain.v3.persisted-feature-intent")]
public sealed record PersistedFeatureIntent(
    [property: Id(0)] string OperationKey,
    [property: Id(1)] FeatureIntentKind Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset? AppliedAt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-schedule-cursor")]
public sealed record FeatureScheduleCursor(
    [property: Id(0)] string ScheduleId,
    [property: Id(1)] DateTimeOffset LastOccurrenceAt,
    [property: Id(2)] DateTimeOffset NextOccurrenceAt);

[GenerateSerializer, Alias("digitalbrain.v3.feature-installation-state")]
public sealed record FeatureInstallationState(
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
    public static FeatureInstallationState Create(
        ReleaseDigest release,
        FeatureInstallationId? installationId = null) =>
        new(installationId ?? new FeatureInstallationId("unbound"), release, null, "{}", false, [], null, [], [], 0, 0, null, []);
}

public sealed record FeatureAppendTransition(FeatureInstallationState State, FeatureAppendStatus Status);

public sealed record FeatureClaimTransition(FeatureInstallationState State, FeatureRunClaim? Claim);

public sealed record FeatureCommitTransition(FeatureInstallationState State, FeatureCompletion Completion);
