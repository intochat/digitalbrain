using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Features;

internal static class FeatureStateEquality
{
    public static bool Same(FeatureInstallationState? left, FeatureInstallationState? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.InstallationId == right.InstallationId && left.ActiveRelease == right.ActiveRelease &&
        left.PreviousRelease == right.PreviousRelease &&
        string.Equals(left.StateJson, right.StateJson, StringComparison.Ordinal) &&
        left.Paused == right.Paused &&
        left.Inbox.SequenceEqual(right.Inbox) &&
        Equals(left.Lease, right.Lease) &&
        left.Completions.SequenceEqual(right.Completions) &&
        left.Intents.SequenceEqual(right.Intents) &&
        left.NextFence == right.NextFence &&
        left.Revision == right.Revision &&
        string.Equals(left.PauseReason, right.PauseReason, StringComparison.Ordinal) &&
        left.Schedules.SequenceEqual(right.Schedules);
    public static bool Same(FeatureHubState? left, FeatureHubState? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.Revision == right.Revision && Same(left.Installations, right.Installations) &&
        Same(left.FanOuts, right.FanOuts) &&
        Same(left.Releases, right.Releases) &&
        Same(left.Approvals, right.Approvals) &&
        Same(left.Authorities, right.Authorities) &&
        left.Alerts.SequenceEqual(right.Alerts);
    private static bool Same(IReadOnlyList<FeatureInstallationRegistration> left, IReadOnlyList<FeatureInstallationRegistration> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.InstallationId == pair.Second.InstallationId && pair.First.Release == pair.Second.Release &&
            pair.First.Subscriptions.SequenceEqual(pair.Second.Subscriptions, StringComparer.Ordinal));
    private static bool Same(IReadOnlyList<FeatureFanOutState> left, IReadOnlyList<FeatureFanOutState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Input == pair.Second.Input && pair.First.Deliveries.SequenceEqual(pair.Second.Deliveries));
    private static bool Same(IReadOnlyList<FeatureReleaseMetadata> left, IReadOnlyList<FeatureReleaseMetadata> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second));
    private static bool Same(FeatureReleaseMetadata left, FeatureReleaseMetadata right) =>
        left.Digest == right.Digest && string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
        left.SourceKind == right.SourceKind &&
        left.RequestedCapabilities.SequenceEqual(right.RequestedCapabilities, StringComparer.Ordinal) &&
        left.Dependencies.SequenceEqual(right.Dependencies, StringComparer.Ordinal);
    private static bool Same(IReadOnlyList<FeatureApprovalState> left, IReadOnlyList<FeatureApprovalState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.ApprovalId, pair.Second.ApprovalId, StringComparison.Ordinal) &&
            pair.First.InstallationId == pair.Second.InstallationId &&
            Same(pair.First.Release, pair.Second.Release) &&
            pair.First.AddedCapabilities.SequenceEqual(pair.Second.AddedCapabilities, StringComparer.Ordinal) &&
            pair.First.RemovedCapabilities.SequenceEqual(pair.Second.RemovedCapabilities, StringComparer.Ordinal) &&
            pair.First.Status == pair.Second.Status &&
            string.Equals(pair.First.DecisionId, pair.Second.DecisionId, StringComparison.Ordinal) &&
            pair.First.DecidedAt == pair.Second.DecidedAt &&
            pair.First.Revision == pair.Second.Revision &&
            Same(pair.First.Grants, pair.Second.Grants));
    private static bool Same(IReadOnlyList<FeatureInstallationAuthorityState> left, IReadOnlyList<FeatureInstallationAuthorityState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.InstallationId == pair.Second.InstallationId && pair.First.ActorId == pair.Second.ActorId &&
            pair.First.ActiveRelease == pair.Second.ActiveRelease &&
            pair.First.PreviousRelease == pair.Second.PreviousRelease &&
            pair.First.ActiveGrantRevision == pair.Second.ActiveGrantRevision &&
            Same(pair.First.ActiveGrants, pair.Second.ActiveGrants) &&
            pair.First.PreviousGrantRevision == pair.Second.PreviousGrantRevision &&
            Same(pair.First.PreviousGrants, pair.Second.PreviousGrants) &&
            pair.First.PendingRelease == pair.Second.PendingRelease &&
            pair.First.PendingGrantRevision == pair.Second.PendingGrantRevision &&
            Same(pair.First.PendingGrants, pair.Second.PendingGrants) &&
            pair.First.Paused == pair.Second.Paused &&
            string.Equals(pair.First.PauseReason, pair.Second.PauseReason, StringComparison.Ordinal));
    private static bool Same(IReadOnlyList<FeatureGrantState> left, IReadOnlyList<FeatureGrantState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.CapabilityId, pair.Second.CapabilityId, StringComparison.Ordinal) &&
            pair.First.CapabilityVersion == pair.Second.CapabilityVersion &&
            pair.First.ProviderConnectionId == pair.Second.ProviderConnectionId &&
            string.Equals(pair.First.ConstraintsJson, pair.Second.ConstraintsJson, StringComparison.Ordinal) &&
            string.Equals(pair.First.Provider, pair.Second.Provider, StringComparison.Ordinal));
}
