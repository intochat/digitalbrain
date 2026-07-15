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

[GenerateSerializer, Alias("digitalbrain.feature.verified-candidate.v1")]
public sealed record VerifiedFeatureCandidate(
    [property: Id(0)] FeatureDraft Draft,
    [property: Id(1)] FeatureReleaseMetadata Release);

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
    [property: Id(3)] string[] Subscriptions);

[GenerateSerializer, Alias("digitalbrain.feature.install-version.v1")]
public sealed record InstallFeatureVersion(
    [property: Id(0)] FeatureDraftId DraftId,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] FeatureInstallationId InstallationId,
    [property: Id(3)] ReleaseDigest Release,
    [property: Id(4)] FeatureGrantSpec[] Grants,
    [property: Id(5)] string[] Subscriptions,
    [property: Id(6)] string DecisionId,
    [property: Id(7)] string IdempotencyId);

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
    [property: Id(8)] ActorId ActorId = default);

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

[Alias("digitalbrain.feature.suggestion-model-grain.v1")]
public interface IFeatureSuggestionModelGrain : IGrainWithStringKey
{
    [Alias("suggest")]
    Task<FeatureDraftPatch> SuggestAsync(SuggestFeatureChange command, CancellationToken cancellationToken = default);
}
