using Orleans;

namespace DigitalBrain.Kernel.Contracts;

[GenerateSerializer, Alias("digitalbrain.feature.draft-patch.v1")]
public sealed record FeatureDraftPatch(
    [property: Id(0)] string PatchId,
    [property: Id(1)] FeatureDraftId DraftId,
    [property: Id(2)] long BaseRevision,
    [property: Id(3)] string Summary,
    [property: Id(4)] FeatureBehavior ReplacementBehavior,
    [property: Id(5)] FeatureSourceSnapshot ReplacementSource);

[GenerateSerializer, Alias("digitalbrain.feature.suggest-change.v1")]
public sealed record SuggestFeatureChange(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] string Guidance,
    [property: Id(3)] string SuggestionId);

[GenerateSerializer, Alias("digitalbrain.feature.accept-suggested-change.v1")]
public sealed record AcceptSuggestedChange(
    [property: Id(0)] FeatureDraftPatch Patch,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] string IdempotencyId,
    [property: Id(3)] DateTimeOffset AcceptedAt);

[GenerateSerializer, Alias("digitalbrain.feature.reject-suggested-change.v1")]
public sealed record RejectSuggestedChange(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] string PatchId,
    [property: Id(2)] long BaseRevision,
    [property: Id(3)] long ExpectedRevision);

[GenerateSerializer, Alias("digitalbrain.feature.verify-draft.v1")]
public sealed record VerifyFeatureDraft(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] string IdempotencyId);

[Alias("digitalbrain.feature.scenario-outcome.v1")]
public enum FeatureScenarioOutcome
{
    Passed,
    Failed,
    Skipped
}

[GenerateSerializer, Alias("digitalbrain.feature.scenario-evidence.v1")]
public sealed record FeatureScenarioEvidence(
    [property: Id(0)] string ScenarioId,
    [property: Id(1)] string Name,
    [property: Id(2)] FeatureScenarioOutcome Outcome,
    [property: Id(3)] string? SafeFailure,
    [property: Id(4)] long DurationMilliseconds);

[GenerateSerializer, Alias("digitalbrain.feature.verification-artifact.v1")]
public sealed record FeatureVerificationArtifact(
    [property: Id(0)] string Name,
    [property: Id(1)] string MediaType,
    [property: Id(2)] long SizeBytes,
    [property: Id(3)] string Digest);

[GenerateSerializer, Alias("digitalbrain.feature.verification-evidence.v1")]
public sealed record FeatureVerificationEvidence(
    [property: Id(0)] string SourceReference,
    [property: Id(1)] int Total,
    [property: Id(2)] int Passed,
    [property: Id(3)] int Failed,
    [property: Id(4)] int Skipped,
    [property: Id(5)] FeatureScenarioEvidence[] Scenarios,
    [property: Id(6)] FeatureVerificationArtifact[] Artifacts);

[GenerateSerializer, Alias("digitalbrain.feature.verified-candidate.v1")]
public sealed record VerifiedFeatureCandidate(
    [property: Id(0)] FeatureDraft Draft,
    [property: Id(1)] FeatureReleaseMetadata Release,
    [property: Id(2)] FeatureVerificationEvidence? Evidence = null);

[GenerateSerializer, Alias("digitalbrain.feature.prepare-access-review.v1")]
public sealed record PrepareFeatureAccessReview(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] FeatureInstallationId InstallationId,
    [property: Id(3)] ReleaseDigest Release,
    [property: Id(4)] FeatureGrantSpec[] Grants,
    [property: Id(5)] string[] Subscriptions);

[GenerateSerializer, Alias("digitalbrain.feature.access-review.v1")]
public sealed record FeatureAccessReview(
    [property: Id(0)] VerifiedFeatureCandidate Candidate,
    [property: Id(1)] FeatureInstallationId InstallationId,
    [property: Id(2)] FeatureGrantSpec[] Grants,
    [property: Id(3)] string[] Subscriptions,
    [property: Id(4)] FeatureReleaseMetadata? PreviousRelease = null);

[GenerateSerializer, Alias("digitalbrain.feature.install-version.v1")]
public sealed record InstallFeatureVersion(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] FeatureInstallationId InstallationId,
    [property: Id(3)] ReleaseDigest Release,
    [property: Id(4)] FeatureGrantSpec[] Grants,
    [property: Id(5)] string[] Subscriptions,
    [property: Id(6)] string DecisionId,
    [property: Id(7)] string IdempotencyId,
    [property: Id(8)] long? RuntimeRevision = null,
    [property: Id(9)] ReleaseDigest? RuntimeActiveRelease = null,
    [property: Id(10)] ReleaseDigest? RuntimePreviousRelease = null);

[GenerateSerializer, Alias("digitalbrain.feature.draft-installation-reservation.v1")]
public sealed record FeatureDraftInstallationReservation(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] long DraftRevision,
    [property: Id(2)] FeatureInstallationId InstallationId,
    [property: Id(3)] ReleaseDigest Release,
    [property: Id(4)] string IdempotencyId,
    [property: Id(5)] string CommandDigest,
    [property: Id(6)] string AccessDigest,
    [property: Id(7)] string DecisionId,
    [property: Id(8)] ActorId ActorId = default,
    [property: Id(9)] FeatureGrantSpec[]? Grants = null,
    [property: Id(10)] string[]? Subscriptions = null,
    [property: Id(11)] long? RuntimeRevision = null,
    [property: Id(12)] ReleaseDigest? RuntimeActiveRelease = null,
    [property: Id(13)] ReleaseDigest? RuntimePreviousRelease = null,
    [property: Id(14)] FeatureInstallationAuthorityBaseline? AuthorityBaseline = null);

[GenerateSerializer, Alias("digitalbrain.feature.installation-authority-baseline.v1")]
public sealed record FeatureInstallationAuthorityBaseline(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ActorId ActorId,
    [property: Id(2)] ReleaseDigest ActiveRelease,
    [property: Id(3)] ReleaseDigest? PreviousRelease,
    [property: Id(4)] GrantRevision ActiveGrantRevision,
    [property: Id(5)] FeatureGrantSpec[] ActiveGrants,
    [property: Id(6)] GrantRevision? PreviousGrantRevision,
    [property: Id(7)] FeatureGrantSpec[] PreviousGrants,
    [property: Id(8)] bool Paused,
    [property: Id(9)] string? PauseReason,
    [property: Id(10)] long PublicationFence,
    [property: Id(11)] FeaturePublicationReceipt? PublicationReceipt,
    [property: Id(12)] string[]? PreviousSubscriptions,
    [property: Id(13)] FeatureInstallationRollbackReplayBaseline? RollbackReplay,
    [property: Id(14)] FeatureInstallationRegistration Registration);

[GenerateSerializer, Alias("digitalbrain.feature.installation-rollback-replay-baseline.v1")]
public sealed record FeatureInstallationRollbackReplayBaseline(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ReleaseDigest ExpectedActiveRelease,
    [property: Id(2)] ReleaseDigest TargetRelease,
    [property: Id(3)] long ExpectedRevision,
    [property: Id(4)] string IdempotencyId,
    [property: Id(5)] string ResultAccessDigest);

[GenerateSerializer, Alias("digitalbrain.feature.reset-draft-installation-reservation.v1")]
public sealed record ResetFeatureDraftInstallationReservation(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] string IdempotencyId,
    [property: Id(2)] InstallFeatureVersion? ReservedInstallation = null);

[GenerateSerializer, Alias("digitalbrain.feature.draft-installation-reset-preparation.v1")]
public sealed record FeatureDraftInstallationResetPreparation(
    [property: Id(0)] FeatureDraft Draft,
    [property: Id(1)] bool Completed,
    [property: Id(2)] bool RequiresRepublish,
    [property: Id(3)] FeatureInstallationRegistration? ActiveRegistration);

[GenerateSerializer, Alias("digitalbrain.feature.draft-installation-reset-obligation.v1")]
public sealed record FeatureDraftInstallationResetObligation(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] string IdempotencyId,
    [property: Id(2)] ActorId ActorId,
    [property: Id(3)] FeatureInstallationId InstallationId,
    [property: Id(4)] ReleaseDigest Release,
    [property: Id(5)] bool RequiresRepublish);

[GenerateSerializer, Alias("digitalbrain.feature.publication-ticket.v1")]
public sealed record FeaturePublicationTicket(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] ActorId ActorId,
    [property: Id(2)] ReleaseDigest Release,
    [property: Id(3)] GrantRevision GrantRevision,
    [property: Id(4)] FeatureGrantSpec[] ActiveGrants,
    [property: Id(5)] string[] Subscriptions,
    [property: Id(6)] long PublicationFence,
    [property: Id(7)] string AuthorityDigest,
    [property: Id(8)] string AccessDigest);

[GenerateSerializer, Alias("digitalbrain.feature.publication-receipt.v1")]
public sealed record FeaturePublicationReceipt(
    [property: Id(0)] FeatureInstallationId InstallationId,
    [property: Id(1)] long PublicationFence,
    [property: Id(2)] string AuthorityDigest,
    [property: Id(3)] string AccessDigest,
    [property: Id(4)] string ManifestDigest);

[GenerateSerializer, Alias("digitalbrain.feature.installed-version.v1")]
public sealed record InstalledFeatureVersion(
    [property: Id(0)] FeatureDraft Draft,
    [property: Id(1)] FeatureReleaseMetadata Release,
    [property: Id(2)] FeatureAuthoritySnapshot Authority,
    [property: Id(3)] FeatureInstallationRegistration Registration);

[GenerateSerializer, Alias("digitalbrain.feature.rollback-version.v1")]
public sealed record RollbackFeatureVersion(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] ReleaseDigest ExpectedActiveRelease,
    [property: Id(2)] ReleaseDigest TargetRelease,
    [property: Id(3)] string IdempotencyId,
    [property: Id(4)] long ExpectedRevision);

[GenerateSerializer, Alias("digitalbrain.feature.installed-detail.v1")]
public sealed record InstalledFeatureDetail(
    [property: Id(0)] FeatureDraft Draft,
    [property: Id(1)] FeatureReleaseMetadata ActiveRelease,
    [property: Id(2)] FeatureReleaseMetadata? PreviousRelease,
    [property: Id(3)] FeatureAuthoritySnapshot Authority,
    [property: Id(4)] FeatureInstallationRegistration Registration,
    [property: Id(5)] long Revision);

[Alias("digitalbrain.feature.suggestion-model-grain.v1")]
public interface IFeatureSuggestionModelGrain : IGrainWithStringKey
{
    [Alias("suggest")]
    Task<FeatureDraftPatch> SuggestAsync(SuggestFeatureChange command, CancellationToken cancellationToken = default);
}
