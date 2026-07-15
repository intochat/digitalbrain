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
        left.Schedules.SequenceEqual(right.Schedules) &&
        left.UnconfirmedReleaseSwitch == right.UnconfirmedReleaseSwitch;
    public static bool Same(FeatureHubState? left, FeatureHubState? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.Revision == right.Revision && Same(left.Installations, right.Installations) &&
        Same(left.FanOuts, right.FanOuts) &&
        Same(left.Releases, right.Releases) &&
        Same(left.Approvals, right.Approvals) &&
        Same(left.Authorities, right.Authorities) &&
        left.Alerts.SequenceEqual(right.Alerts) &&
        Same(left.Drafts ?? [], right.Drafts ?? []) &&
        Same(left.DraftReplays ?? [], right.DraftReplays ?? []) &&
        Same(left.DraftInstallationReservations ?? [], right.DraftInstallationReservations ?? []) &&
        (left.DraftInstallationResets ?? []).SequenceEqual(right.DraftInstallationResets ?? []);
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
        left.Dependencies.SequenceEqual(right.Dependencies, StringComparer.Ordinal) &&
        Same(left.Source, right.Source);
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
            pair.First.DecisionActorId == pair.Second.DecisionActorId &&
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
            string.Equals(pair.First.PauseReason, pair.Second.PauseReason, StringComparison.Ordinal) &&
            pair.First.PublicationFence == pair.Second.PublicationFence &&
            pair.First.PublicationReceipt == pair.Second.PublicationReceipt &&
            Same(pair.First.PreviousSubscriptions, pair.Second.PreviousSubscriptions) &&
            Same(pair.First.RollbackReplay, pair.Second.RollbackReplay));
    private static bool Same(IReadOnlyList<FeatureGrantState> left, IReadOnlyList<FeatureGrantState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            string.Equals(pair.First.CapabilityId, pair.Second.CapabilityId, StringComparison.Ordinal) &&
            pair.First.CapabilityVersion == pair.Second.CapabilityVersion &&
            pair.First.ProviderConnectionId == pair.Second.ProviderConnectionId &&
            string.Equals(pair.First.ConstraintsJson, pair.Second.ConstraintsJson, StringComparison.Ordinal) &&
            string.Equals(pair.First.Provider, pair.Second.Provider, StringComparison.Ordinal));
    private static bool Same(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.SequenceEqual(right, StringComparer.Ordinal);
    private static bool Same(FeatureRollbackReplay? left, FeatureRollbackReplay? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.InstallationId == right.InstallationId &&
        left.ExpectedActiveRelease == right.ExpectedActiveRelease && left.TargetRelease == right.TargetRelease &&
        left.ExpectedRevision == right.ExpectedRevision &&
        string.Equals(left.IdempotencyId, right.IdempotencyId, StringComparison.Ordinal) &&
        string.Equals(left.ResultAccessDigest, right.ResultAccessDigest, StringComparison.Ordinal);
    private static bool Same(IReadOnlyList<FeatureDraft> left, IReadOnlyList<FeatureDraft> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second));
    private static bool Same(FeatureDraft left, FeatureDraft right) =>
        left.DraftId == right.DraftId && left.OriginatingRequest == right.OriginatingRequest &&
        string.Equals(left.Goal, right.Goal, StringComparison.Ordinal) &&
        string.Equals(left.Status, right.Status, StringComparison.Ordinal) &&
        Same(left.Behavior, right.Behavior) && Same(left.Source, right.Source) &&
        Same(left.Verification, right.Verification) && left.InstallationId == right.InstallationId &&
        left.Revision == right.Revision && left.CreatedAt == right.CreatedAt && left.UpdatedAt == right.UpdatedAt;
    private static bool Same(FeatureBehavior left, FeatureBehavior right) =>
        left.Scenarios.Length == right.Scenarios.Length && left.Scenarios.Zip(right.Scenarios).All(pair => pair.First == pair.Second);
    private static bool Same(FeatureSourceSnapshot? left, FeatureSourceSnapshot? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        string.Equals(left.ImplementationProjectPath, right.ImplementationProjectPath, StringComparison.Ordinal) &&
        string.Equals(left.ScenarioProjectPath, right.ScenarioProjectPath, StringComparison.Ordinal) &&
        left.Files.Length == right.Files.Length && left.Files.Zip(right.Files).All(pair => pair.First == pair.Second);
    private static bool Same(FeatureVerification? left, FeatureVerification? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.Release == right.Release && left.Total == right.Total &&
        left.Passed == right.Passed && left.Failed == right.Failed && left.Skipped == right.Skipped &&
        left.VerifiedAt == right.VerifiedAt && Same(left.Evidence, right.Evidence);
    private static bool Same(FeatureVerificationEvidence? left, FeatureVerificationEvidence? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
        left.Total == right.Total && left.Passed == right.Passed && left.Failed == right.Failed && left.Skipped == right.Skipped &&
        left.Scenarios.SequenceEqual(right.Scenarios) && left.Artifacts.SequenceEqual(right.Artifacts);
    private static bool Same(IReadOnlyList<FeatureDraftCommandReplay> left, IReadOnlyList<FeatureDraftCommandReplay> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.DraftId == pair.Second.DraftId &&
            string.Equals(pair.First.IdempotencyId, pair.Second.IdempotencyId, StringComparison.Ordinal) &&
            string.Equals(pair.First.Kind, pair.Second.Kind, StringComparison.Ordinal) &&
            string.Equals(pair.First.PayloadDigest, pair.Second.PayloadDigest, StringComparison.Ordinal) &&
            string.Equals(pair.First.ResultStatus, pair.Second.ResultStatus, StringComparison.Ordinal) &&
            Same(pair.First.ResultBehavior, pair.Second.ResultBehavior) &&
            Same(pair.First.ResultSource, pair.Second.ResultSource) &&
            Same(pair.First.ResultVerification, pair.Second.ResultVerification) &&
            pair.First.ResultInstallationId == pair.Second.ResultInstallationId &&
            pair.First.ResultRevision == pair.Second.ResultRevision &&
            pair.First.ResultUpdatedAt == pair.Second.ResultUpdatedAt &&
            pair.First.Utf8Bytes == pair.Second.Utf8Bytes &&
            pair.First.ActorId == pair.Second.ActorId);
    private static bool Same(IReadOnlyList<FeatureDraftInstallationReservation> left, IReadOnlyList<FeatureDraftInstallationReservation> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.DraftId == pair.Second.DraftId &&
            pair.First.DraftRevision == pair.Second.DraftRevision &&
            pair.First.InstallationId == pair.Second.InstallationId &&
            pair.First.Release == pair.Second.Release &&
            string.Equals(pair.First.IdempotencyId, pair.Second.IdempotencyId, StringComparison.Ordinal) &&
            string.Equals(pair.First.CommandDigest, pair.Second.CommandDigest, StringComparison.Ordinal) &&
            string.Equals(pair.First.AccessDigest, pair.Second.AccessDigest, StringComparison.Ordinal) &&
            string.Equals(pair.First.DecisionId, pair.Second.DecisionId, StringComparison.Ordinal) &&
            pair.First.ActorId == pair.Second.ActorId &&
            pair.First.RuntimeRevision == pair.Second.RuntimeRevision &&
            pair.First.RuntimeActiveRelease == pair.Second.RuntimeActiveRelease &&
            pair.First.RuntimePreviousRelease == pair.Second.RuntimePreviousRelease &&
            SameReservationGrants(pair.First.Grants, pair.Second.Grants) &&
            Same(pair.First.Subscriptions, pair.Second.Subscriptions) &&
            Same(pair.First.AuthorityBaseline, pair.Second.AuthorityBaseline));
    private static bool SameReservationGrants(FeatureGrantSpec[]? left, FeatureGrantSpec[]? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null && left.SequenceEqual(right);
    private static bool Same(FeatureInstallationAuthorityBaseline? left, FeatureInstallationAuthorityBaseline? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        left.InstallationId == right.InstallationId && left.ActorId == right.ActorId &&
        left.ActiveRelease == right.ActiveRelease && left.PreviousRelease == right.PreviousRelease &&
        left.ActiveGrantRevision == right.ActiveGrantRevision && left.ActiveGrants.SequenceEqual(right.ActiveGrants) &&
        left.PreviousGrantRevision == right.PreviousGrantRevision && left.PreviousGrants.SequenceEqual(right.PreviousGrants) &&
        left.Paused == right.Paused && string.Equals(left.PauseReason, right.PauseReason, StringComparison.Ordinal) &&
        left.PublicationFence == right.PublicationFence && left.PublicationReceipt == right.PublicationReceipt &&
        Same(left.PreviousSubscriptions, right.PreviousSubscriptions) && left.RollbackReplay == right.RollbackReplay &&
        left.Registration.InstallationId == right.Registration.InstallationId &&
        left.Registration.Release == right.Registration.Release &&
        left.Registration.Subscriptions.SequenceEqual(right.Registration.Subscriptions, StringComparer.Ordinal);
}
