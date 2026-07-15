using System.Text;
using System.Text.Json;
using Azure;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class FeatureAuthoringService(
    IClusterClient cluster,
    IFeatureBuildEndpoint builds,
    IFeatureArtifactCatalog artifacts,
    IFeatureLifecycleRail lifecycle,
    TimeProvider timeProvider)
{
    public async Task<FeatureDraft> ReadAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(draftId);
        return await ReadDraftAsync(Hub(context), draftId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureDraft> ReviseBehaviorAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        FeatureBehavior behavior,
        long expectedRevision,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        DemandIncrementableRevision(expectedRevision);
        return await Hub(context).ReviseBehaviorAsync(new ReviseFeatureBehavior(
                draftId,
                behavior,
                expectedRevision,
                idempotencyId,
                timeProvider.GetUtcNow()))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FeatureDraft> ReviseSourceAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        FeatureSourceSnapshot source,
        long expectedRevision,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        DemandIncrementableRevision(expectedRevision);
        return await Hub(context).ReviseSourceAsync(new ReviseFeatureSource(
                draftId,
                source,
                expectedRevision,
                idempotencyId,
                timeProvider.GetUtcNow()))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<FeatureDraft> AcceptSuggestedChangeAsync(
        RuntimeRequestContext context,
        FeatureDraftPatch patch,
        long expectedRevision,
        string idempotencyId,
        CancellationToken cancellationToken = default) =>
        AcceptSuggestedChangeAsync(
            context,
            new AcceptSuggestedChange(
                patch,
                expectedRevision,
                idempotencyId,
                timeProvider.GetUtcNow()),
            cancellationToken);

    public async Task<FeatureDraft> AcceptSuggestedChangeAsync(
        RuntimeRequestContext context,
        AcceptSuggestedChange command,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(command);
        DemandIncrementableRevision(command.ExpectedRevision);
        return await Hub(context).AcceptSuggestedChangeAsync(command)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FeatureDraft> RejectSuggestedChangeAsync(
        RuntimeRequestContext context,
        RejectSuggestedChange command,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(command);
        return await Hub(context).RejectSuggestedChangeAsync(command)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<VerifiedFeatureCandidate> VerifyAsync(
        RuntimeRequestContext context,
        VerifyFeatureDraft command,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(command);
        DemandIncrementableRevision(command.ExpectedRevision);
        var hub = Hub(context);
        var draft = await ReadDraftAsync(hub, command.DraftId, cancellationToken).ConfigureAwait(false);
        var verificationReplay = IsSingleRevisionReplay(command.ExpectedRevision, draft.Revision) && draft.Verification is not null;
        if (draft.Revision != command.ExpectedRevision && !verificationReplay)
            throw Rejected(FeatureCommandRejectionReason.Conflict);
        if (!string.Equals(draft.Status, "draft", StringComparison.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var source = draft.Source;
        var artifact = await BuildAsync(
            new FeatureBuildSubmission(
                source.ImplementationProjectPath,
                source.ScenarioProjectPath,
                source.Files.Select(file => new FeatureSourceInput(file.Path, file.Content)).ToArray(),
                FeatureSourceKind.RuntimeAuthored),
            cancellationToken).ConfigureAwait(false);
        DemandPassingArtifact(artifact);
        var verification = new FeatureVerification(
            artifact.Release.Digest,
            artifact.Scenarios.Total,
            artifact.Scenarios.Passed,
            artifact.Scenarios.Failed,
            artifact.Scenarios.Skipped,
            verificationReplay ? draft.Verification!.VerifiedAt : timeProvider.GetUtcNow());
        var recorded = await hub.RecordVerificationAsync(new RecordFeatureVerification(
                command.DraftId,
                verification,
                command.ExpectedRevision,
                command.IdempotencyId))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return new VerifiedFeatureCandidate(recorded, artifact.Release);
    }

    public async Task<FeatureAccessReview> PrepareAccessReviewAsync(
        RuntimeRequestContext context,
        PrepareFeatureAccessReview command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reviewed = await ReviewAsync(
            context,
            command.DraftId,
            command.ExpectedRevision,
            command.InstallationId,
            command.Release,
            command.Grants,
            command.Subscriptions,
            allowInstalledReplay: false,
            cancellationToken).ConfigureAwait(false);
        var inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        return new FeatureAccessReview(
            new VerifiedFeatureCandidate(reviewed.Draft, reviewed.Release),
            reviewed.InstallationId,
            reviewed.Grants,
            reviewed.Subscriptions);
    }

    public async Task<InstalledFeatureVersion> InstallAsync(
        RuntimeRequestContext context,
        InstallFeatureVersion command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        FeatureSuggestionService.DemandFeatureAuthor(context);
        DemandIncrementableRevision(command.ExpectedRevision);
        DemandIdentifier(command.DecisionId, nameof(command.DecisionId));
        DemandIdentifier(command.IdempotencyId, nameof(command.IdempotencyId));
        var reviewed = await ReviewAsync(
            context,
            command.DraftId,
            command.ExpectedRevision,
            command.InstallationId,
            command.Release,
            command.Grants,
            command.Subscriptions,
            allowInstalledReplay: true,
            cancellationToken).ConfigureAwait(false);
        var registration = new FeatureInstallationRegistration(
            reviewed.InstallationId,
            reviewed.Release.Digest,
            reviewed.Subscriptions);
        var inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        if (reviewed.InstalledReplay)
        {
            DemandApprovedDecision(reviewed, inspection, command.DecisionId);
            DemandExactActiveInstallation(context, reviewed, inspection);
            var replayedAuthority = await LifecycleAsync(() =>
                lifecycle.RepublishAsync(context, registration, cancellationToken)).ConfigureAwait(false);
            DemandExactAuthority(context, reviewed, replayedAuthority);
            inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
            DemandExistingCoordinate(context, reviewed, inspection);
            var replayed = DemandExactActiveInstallation(context, reviewed, inspection);
            var replayedDraft = await Hub(context).MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
                    command.DraftId,
                    reviewed.InstallationId,
                    reviewed.Release.Digest,
                    command.ExpectedRevision,
                    command.IdempotencyId,
                    reviewed.Draft.UpdatedAt))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return new InstalledFeatureVersion(replayedDraft, reviewed.Release, replayed.Authority, registration);
        }

        var canonicalCommand = command with
        {
            Grants = reviewed.Grants,
            Subscriptions = reviewed.Subscriptions
        };
        await Hub(context).AcquireDraftInstallationReservationAsync(canonicalCommand, context.ActorId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        var approval = ExactApproval(reviewed, inspection);
        if (approval is null)
        {
            approval = await LifecycleAsync(() => lifecycle.ProposeAsync(
                context,
                new FeatureReleaseProposal(reviewed.InstallationId, reviewed.Release, reviewed.Grants),
                inspection.Revision,
                cancellationToken)).ConfigureAwait(false);
        }

        inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        approval = ExactApproval(reviewed, inspection)
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (approval.Status == FeatureApprovalStatus.Pending)
        {
            approval = await LifecycleAsync(() => lifecycle.DecideAsync(
                context,
                new FeatureApprovalDecision(approval.ApprovalId, reviewed.Release.Digest, true, command.DecisionId),
                inspection.Revision,
                cancellationToken)).ConfigureAwait(false);
        }
        else if (approval.Status != FeatureApprovalStatus.Approved ||
                 !string.Equals(approval.DecisionId, command.DecisionId, StringComparison.Ordinal))
        {
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        }

        inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        approval = ExactApproval(reviewed, inspection)
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (approval.Status != FeatureApprovalStatus.Approved ||
            !string.Equals(approval.DecisionId, command.DecisionId, StringComparison.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var installation = ExactInstallation(reviewed.InstallationId, inspection);
        if (!HasReviewedAuthority(context, reviewed, installation))
        {
            await LifecycleAsync(() => lifecycle.GrantAsync(
                context,
                reviewed.InstallationId,
                reviewed.Release.Digest,
                reviewed.Grants,
                inspection.Revision,
                cancellationToken)).ConfigureAwait(false);
        }

        inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        installation = ExactInstallation(reviewed.InstallationId, inspection)
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        FeatureAuthoritySnapshot authority;
        if (installation.Authority.ActiveRelease == reviewed.Release.Digest)
        {
            DemandSameGrants(installation.Authority.ActiveGrants, reviewed.Grants);
            DemandSameRegistration(installation.Registration, registration);
            authority = await LifecycleAsync(() =>
                lifecycle.RepublishAsync(context, registration, cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            if (installation.Authority.PendingRelease != reviewed.Release.Digest)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            DemandSameGrants(installation.Authority.PendingGrants, reviewed.Grants);
            authority = await LifecycleAsync(() =>
                lifecycle.InstallAsync(context, registration, inspection.Revision, cancellationToken)).ConfigureAwait(false);
        }
        DemandExactAuthority(context, reviewed, authority);
        inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        var active = DemandExactActiveInstallation(context, reviewed, inspection);
        var installedDraft = await Hub(context).MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
                command.DraftId,
                reviewed.InstallationId,
                reviewed.Release.Digest,
                command.ExpectedRevision,
                command.IdempotencyId,
                timeProvider.GetUtcNow()))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return new InstalledFeatureVersion(installedDraft, reviewed.Release, active.Authority, registration);
    }

    private IFeatureHubGrain Hub(RuntimeRequestContext context) =>
        cluster.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));

    private static async Task<FeatureDraft> ReadDraftAsync(
        IFeatureHubGrain hub,
        FeatureDraftId draftId,
        CancellationToken cancellationToken) =>
        await hub.ReadDraftAsync(draftId).WaitAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException("The Feature Draft was not found.");

    private static void DemandRevision(FeatureDraft draft, long expectedRevision)
    {
        if (draft.Revision != expectedRevision)
            throw Rejected(FeatureCommandRejectionReason.Conflict);
    }

    private static void DemandPassingArtifact(FeatureBuildArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(artifact.Release);
        ArgumentNullException.ThrowIfNull(artifact.Scenarios);
        if (artifact.Release.SourceKind != FeatureSourceKind.RuntimeAuthored ||
            string.IsNullOrWhiteSpace(artifact.Release.Digest.Value))
            throw new InvalidDataException("FeatureBuilder returned an invalid runtime-authored release.");
        if (artifact.Scenarios.Total <= 0 ||
            artifact.Scenarios.Passed != artifact.Scenarios.Total ||
            artifact.Scenarios.Failed != 0 ||
            artifact.Scenarios.Skipped != 0)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
    }

    private async Task<ReviewedInstallation> ReviewAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        long expectedRevision,
        FeatureInstallationId installationId,
        ReleaseDigest releaseDigest,
        FeatureGrantSpec[] grants,
        string[] subscriptions,
        bool allowInstalledReplay,
        CancellationToken cancellationToken)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        if (string.IsNullOrWhiteSpace(installationId.Value))
            throw new ArgumentException("A Feature installation identity is required.", nameof(installationId));
        var draft = await ReadDraftAsync(Hub(context), draftId, cancellationToken).ConfigureAwait(false);
        var installedReplay = string.Equals(draft.Status, "installed", StringComparison.Ordinal);
        if (installedReplay)
        {
            if (!allowInstalledReplay || draft.InstallationId != installationId)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            if (!IsSingleRevisionReplay(expectedRevision, draft.Revision))
                throw Rejected(FeatureCommandRejectionReason.Conflict);
        }
        else
        {
            DemandRevision(draft, expectedRevision);
            if (!string.Equals(draft.Status, "draft", StringComparison.Ordinal))
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            if (draft.InstallationId is { } existingInstallation && existingInstallation != installationId)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
        }
        var verification = draft.Verification
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (verification.Release != releaseDigest)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (verification.Total <= 0 || verification.Passed != verification.Total || verification.Failed != 0 || verification.Skipped != 0)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var release = await ArtifactAsync(() =>
            artifacts.DemandReleaseAsync(releaseDigest, cancellationToken)).ConfigureAwait(false);
        if (release.Digest != releaseDigest || release.SourceKind != FeatureSourceKind.RuntimeAuthored)
            throw new InvalidDataException("The published Feature release does not match the verified runtime-authored digest.");
        var reviewedGrants = ValidateGrants(release, grants);
        var reviewedSubscriptions = ValidateSubscriptions(subscriptions);
        return new ReviewedInstallation(draft, release, installationId, reviewedGrants, reviewedSubscriptions, installedReplay);
    }

    private static FeatureGrantSpec[] ValidateGrants(FeatureReleaseMetadata release, FeatureGrantSpec[] grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Length > 32)
            throw new ArgumentException("A Feature cannot review more than 32 capability grants.", nameof(grants));
        var seen = new HashSet<(string, int)>();
        foreach (var grant in grants)
        {
            ArgumentNullException.ThrowIfNull(grant);
            if (string.IsNullOrWhiteSpace(grant.CapabilityId) ||
                grant.CapabilityId.Length > 256 ||
                grant.CapabilityId.Any(char.IsControl) ||
                !string.Equals(grant.CapabilityId, grant.CapabilityId.Trim(), StringComparison.Ordinal) ||
                grant.CapabilityVersion < 1 ||
                !seen.Add((grant.CapabilityId, grant.CapabilityVersion)))
                throw new ArgumentException("Canonical unique Feature capability grants are required.", nameof(grants));
            if (grant.ConstraintsJson is null || Encoding.UTF8.GetByteCount(grant.ConstraintsJson) > 65_536)
                throw new ArgumentException("A bounded Feature capability constraint is required.", nameof(grants));
            try
            {
                using var document = JsonDocument.Parse(grant.ConstraintsJson);
                var constraints = CapabilityGrantConstraintPolicy.CopyValidated(document.RootElement);
                if (!CapabilityGrantConstraintPolicy.AllowsTool(constraints, grant.CapabilityId))
                    throw new ArgumentException("Feature capability constraints must allow the exact capability.", nameof(grants));
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Feature capability constraints must be valid JSON objects.", nameof(grants), exception);
            }
            if (grant.Provider is null != grant.ProviderConnectionId is null ||
                grant.Provider is { } provider &&
                (string.IsNullOrWhiteSpace(provider) || provider.Length > 64 || provider.Any(char.IsControl) ||
                 !string.Equals(provider, provider.Trim(), StringComparison.Ordinal)))
                throw new ArgumentException("A provider and connection must be reviewed together.", nameof(grants));
        }
        var requested = release.RequestedCapabilities
            ?? throw new InvalidDataException("The published Feature release has no capability manifest.");
        if (requested.Length != grants.Length ||
            requested.Distinct(StringComparer.Ordinal).Count() != requested.Length ||
            !requested.Order(StringComparer.Ordinal).SequenceEqual(grants.Select(grant => grant.CapabilityId).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ArgumentException("The review must bind exactly one grant for every requested capability.", nameof(grants));
        return grants.OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
            .ThenBy(grant => grant.CapabilityVersion)
            .ToArray();
    }

    private static string[] ValidateSubscriptions(string[] subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        if (subscriptions.Length is 0 or > 64 ||
            subscriptions.Any(subscription => string.IsNullOrWhiteSpace(subscription) ||
                                              subscription.Length > 256 ||
                                              subscription.Any(char.IsControl) ||
                                              !string.Equals(subscription, subscription.Trim(), StringComparison.Ordinal)) ||
            subscriptions.Distinct(StringComparer.Ordinal).Count() != subscriptions.Length)
            throw new ArgumentException("Canonical unique Feature subscriptions are required.", nameof(subscriptions));
        return subscriptions.Order(StringComparer.Ordinal).ToArray();
    }

    private static void DemandExistingCoordinate(
        RuntimeRequestContext context,
        ReviewedInstallation reviewed,
        FeatureLifecycleInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(inspection.Registrations);
        var installationApprovals = inspection.Approvals.Where(candidate =>
            candidate.InstallationId == reviewed.InstallationId).ToArray();
        var approvals = installationApprovals.Where(candidate =>
            candidate.Release.Digest == reviewed.Release.Digest).ToArray();
        var registrations = inspection.Registrations.Where(candidate => candidate.InstallationId == reviewed.InstallationId).ToArray();
        if (registrations.Length > 1)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var matching = inspection.Installations.Where(candidate => candidate.Authority.InstallationId == reviewed.InstallationId).ToArray();
        if (matching.Length > 1)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var hasCurrentReleaseCoordinate = registrations.Length == 1 || matching.Any(candidate =>
            candidate.Authority.ActiveRelease is not null || candidate.Authority.PendingRelease is not null);
        if (!hasCurrentReleaseCoordinate && installationApprovals.Length > 0 &&
            installationApprovals.OrderByDescending(candidate => candidate.Revision).First().Release.Digest != reviewed.Release.Digest)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (approvals.Length > 1)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (approvals.Length == 1)
        {
            if (!SameRelease(approvals[0].Release, reviewed.Release))
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            DemandSameGrants(approvals[0].Grants, reviewed.Grants);
        }
        if (inspection.Approvals.Any(candidate =>
                candidate.Release.Digest == reviewed.Release.Digest && candidate.InstallationId != reviewed.InstallationId) ||
            inspection.Installations.Any(candidate =>
                candidate.Authority.InstallationId != reviewed.InstallationId &&
                (candidate.Authority.ActiveRelease == reviewed.Release.Digest || candidate.Authority.PendingRelease == reviewed.Release.Digest)) ||
            inspection.Registrations.Any(candidate =>
                candidate.Release == reviewed.Release.Digest && candidate.InstallationId != reviewed.InstallationId))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (registrations.Length == 1)
        {
            if (registrations[0].Release != reviewed.Release.Digest)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            DemandSameRegistration(
                registrations[0],
                new FeatureInstallationRegistration(reviewed.InstallationId, reviewed.Release.Digest, reviewed.Subscriptions));
        }
        if (matching.Length == 0) return;
        var installation = matching[0];
        if (installation.Authority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (installation.Authority.ActiveRelease is { } activeRelease && activeRelease != reviewed.Release.Digest ||
            installation.Authority.PendingRelease is { } pendingRelease && pendingRelease != reviewed.Release.Digest)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (installation.Authority.PendingRelease == reviewed.Release.Digest)
            DemandSameGrants(installation.Authority.PendingGrants, reviewed.Grants);
        if (installation.Authority.ActiveRelease == reviewed.Release.Digest)
        {
            DemandSameGrants(installation.Authority.ActiveGrants, reviewed.Grants);
            DemandSameRegistration(
                installation.Registration,
                new FeatureInstallationRegistration(reviewed.InstallationId, reviewed.Release.Digest, reviewed.Subscriptions));
        }
    }

    private static FeatureApprovalSnapshot? ExactApproval(ReviewedInstallation reviewed, FeatureLifecycleInspection inspection)
    {
        var approval = inspection.Approvals.SingleOrDefault(candidate =>
            candidate.InstallationId == reviewed.InstallationId && candidate.Release.Digest == reviewed.Release.Digest);
        if (approval is not null && approval.Status == FeatureApprovalStatus.Rejected)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        return approval;
    }

    private static FeatureInstallationInspection? ExactInstallation(
        FeatureInstallationId installationId,
        FeatureLifecycleInspection inspection) =>
        inspection.Installations.SingleOrDefault(candidate => candidate.Authority.InstallationId == installationId);

    private static void DemandApprovedDecision(
        ReviewedInstallation reviewed,
        FeatureLifecycleInspection inspection,
        string decisionId)
    {
        var approval = ExactApproval(reviewed, inspection)
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (approval.Status != FeatureApprovalStatus.Approved ||
            !string.Equals(approval.DecisionId, decisionId, StringComparison.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
    }

    private static FeatureInstallationInspection DemandExactActiveInstallation(
        RuntimeRequestContext context,
        ReviewedInstallation reviewed,
        FeatureLifecycleInspection inspection)
    {
        var installation = ExactInstallation(reviewed.InstallationId, inspection)
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        DemandExactAuthority(context, reviewed, installation.Authority);
        if (installation.Authority.PendingRelease is not null ||
            installation.Authority.PendingGrantRevision is not null ||
            installation.Authority.PendingGrants.Length != 0)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        DemandSameRegistration(
            installation.Registration,
            new FeatureInstallationRegistration(reviewed.InstallationId, reviewed.Release.Digest, reviewed.Subscriptions));
        if (installation.Runtime is null ||
            installation.Runtime.InstallationId != reviewed.InstallationId ||
            installation.Runtime.ActiveRelease != reviewed.Release.Digest)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        return installation;
    }

    private static void DemandExactAuthority(
        RuntimeRequestContext context,
        ReviewedInstallation reviewed,
        FeatureAuthoritySnapshot authority)
    {
        if (authority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (authority.InstallationId != reviewed.InstallationId ||
            authority.ActiveRelease != reviewed.Release.Digest ||
            authority.ActiveGrantRevision is null)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        DemandSameGrants(authority.ActiveGrants, reviewed.Grants);
    }

    private static bool HasReviewedAuthority(
        RuntimeRequestContext context,
        ReviewedInstallation reviewed,
        FeatureInstallationInspection? installation)
    {
        if (installation is null) return false;
        if (installation.Authority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (installation.Authority.ActiveRelease == reviewed.Release.Digest)
        {
            DemandSameGrants(installation.Authority.ActiveGrants, reviewed.Grants);
            return true;
        }
        if (installation.Authority.PendingRelease == reviewed.Release.Digest)
        {
            DemandSameGrants(installation.Authority.PendingGrants, reviewed.Grants);
            return true;
        }
        if (installation.Authority.PendingRelease is not null)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        return false;
    }

    private static void DemandSameRegistration(
        FeatureInstallationRegistration? actual,
        FeatureInstallationRegistration expected)
    {
        if (actual is null ||
            actual.InstallationId != expected.InstallationId ||
            actual.Release != expected.Release ||
            !actual.Subscriptions.Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Subscriptions.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
    }

    private static void DemandSameGrants(IReadOnlyList<FeatureGrantSpec> actual, IReadOnlyList<FeatureGrantSpec> expected)
    {
        var left = actual.OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal).ThenBy(grant => grant.CapabilityVersion).ToArray();
        var right = expected.OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal).ThenBy(grant => grant.CapabilityVersion).ToArray();
        if (left.Length != right.Length || left.Zip(right).Any(pair =>
                !string.Equals(pair.First.CapabilityId, pair.Second.CapabilityId, StringComparison.Ordinal) ||
                pair.First.CapabilityVersion != pair.Second.CapabilityVersion ||
                pair.First.ProviderConnectionId != pair.Second.ProviderConnectionId ||
                !string.Equals(pair.First.ConstraintsJson, pair.Second.ConstraintsJson, StringComparison.Ordinal) ||
                !string.Equals(pair.First.Provider, pair.Second.Provider, StringComparison.Ordinal)))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
    }

    private static bool SameRelease(FeatureReleaseMetadata left, FeatureReleaseMetadata right) =>
        left.Digest == right.Digest &&
        string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
        left.SourceKind == right.SourceKind &&
        left.RequestedCapabilities.SequenceEqual(right.RequestedCapabilities, StringComparer.Ordinal) &&
        left.Dependencies.SequenceEqual(right.Dependencies, StringComparer.Ordinal);

    private static void DemandIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical command identifier is required.", parameterName);
    }

    private static void DemandIncrementableRevision(long revision)
    {
        if (revision == long.MaxValue)
            throw Rejected(FeatureCommandRejectionReason.Conflict);
    }

    private async Task<FeatureBuildArtifact> BuildAsync(
        FeatureBuildSubmission submission,
        CancellationToken cancellationToken)
    {
        try
        {
            return await builds.BuildAsync(submission, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureCommandRejectedException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (RequestFailedException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (IOException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (TimeoutException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (OrleansException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
    }

    private static async Task<T> ArtifactAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureCommandRejectedException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (RequestFailedException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (IOException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (TimeoutException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (OrleansException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
    }

    private static async Task<T> LifecycleAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureCommandRejectedException)
        {
            throw;
        }
        catch (FeatureAuthorityRejectedException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        }
        catch (RequestFailedException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (IOException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (TimeoutException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (OrleansException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
    }

    private static bool IsSingleRevisionReplay(long expectedRevision, long actualRevision) =>
        expectedRevision != long.MaxValue && actualRevision == expectedRevision + 1;

    private static FeatureCommandRejectedException Rejected(FeatureCommandRejectionReason reason) => new(reason);

    private sealed record ReviewedInstallation(
        FeatureDraft Draft,
        FeatureReleaseMetadata Release,
        FeatureInstallationId InstallationId,
        FeatureGrantSpec[] Grants,
        string[] Subscriptions,
        bool InstalledReplay);
}
