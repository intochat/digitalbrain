using System.Text;
using System.Text.Json;
using Azure;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record FeatureVerificationReview(
    FeatureDraft Draft,
    FeatureReleaseMetadata? Release,
    FeatureVerificationEvidence Evidence,
    DateTimeOffset AttemptedAt);

public sealed record FeatureDraftRecoverySnapshot(
    FeatureDraft Draft,
    FeatureInstallationRecoverySnapshot? Recovery);

public sealed record FeatureInstallationRecoverySnapshot(
    bool Installed,
    FeatureVerification Verification,
    FeatureReleaseMetadata Release,
    FeatureInstallationId InstallationId,
    FeatureGrantSpec[] Grants,
    string[] Subscriptions,
    FeatureReleaseMetadata? PreviousRelease,
    string? DecisionId,
    string? IdempotencyId,
    bool RollbackAvailable,
    bool Paused,
    string? PauseReason);

public sealed class FeatureAuthoringService(
    IClusterClient cluster,
    IFeatureBuildEndpoint builds,
    IFeatureArtifactCatalog artifacts,
    IFeatureLifecycleRail lifecycle,
    TimeProvider timeProvider,
    IFeatureCapabilityCatalog? capabilityCatalog = null)
{
    private const int MaximumVerificationEvidenceUtf8Bytes = 2 * 1024 * 1024;

    public async Task<FeatureDraft> ReadAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(draftId);
        return await ReadDraftAsync(Hub(context), draftId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureDraftRecoverySnapshot> ReadWithRecoveryAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(draftId);
        var hub = Hub(context);
        var draft = await ReadDraftAsync(hub, draftId, cancellationToken).ConfigureAwait(false);
        var reset = await hub.ReadDraftInstallationResetAsync(draftId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (reset is not null)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var reservation = await hub.ReadDraftInstallationReservationAsync(draftId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(draft.Status, "draft", StringComparison.Ordinal))
        {
            return reservation is null
                ? new FeatureDraftRecoverySnapshot(draft, null)
                : await ReservedRecoveryAsync(context, draft, reservation, cancellationToken).ConfigureAwait(false);
        }
        if (!string.Equals(draft.Status, "installed", StringComparison.Ordinal) || reservation is not null)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        return await InstalledRecoveryAsync(context, draft, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureDraftRecoverySnapshot> ResetInstallationReservationAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(draftId);
        DemandIdentifier(idempotencyId, nameof(idempotencyId));
        var hub = Hub(context);
        var effectiveIdempotencyId = idempotencyId;
        var reset = await hub.ReadDraftInstallationResetAsync(draftId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (reset is not null)
        {
            if (reset.ActorId != context.ActorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            effectiveIdempotencyId = reset.IdempotencyId;
        }
        var reservation = await hub.ReadDraftInstallationReservationAsync(draftId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        InstallFeatureVersion? reservedInstallation = null;
        if (reservation is not null)
        {
            if (reservation.ActorId != context.ActorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            if (reservation.Grants is not { } grants || reservation.Subscriptions is not { } subscriptions)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            reservedInstallation = new InstallFeatureVersion(
                reservation.DraftId,
                reservation.DraftRevision,
                reservation.InstallationId,
                reservation.Release,
                grants,
                subscriptions,
                reservation.DecisionId,
                reservation.IdempotencyId,
                reservation.RuntimeRevision,
                reservation.RuntimeActiveRelease,
                reservation.RuntimePreviousRelease);
        }
        if (reset is not null &&
            (reservation is null || reservation.InstallationId != reset.InstallationId ||
             reservation.Release != reset.Release))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var prepared = await hub.ResetDraftInstallationReservationAsync(
                new ResetFeatureDraftInstallationReservation(draftId, effectiveIdempotencyId, reservedInstallation),
                context.ActorId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (prepared.Completed)
            return new FeatureDraftRecoverySnapshot(prepared.Draft, null);
        if (!prepared.RequiresRepublish || prepared.ActiveRegistration is not { } registration)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        await LifecycleAsync(() => lifecycle.RepublishAsync(context, registration, cancellationToken)).ConfigureAwait(false);
        var completed = await hub.CompleteDraftInstallationReservationResetAsync(
                draftId,
                effectiveIdempotencyId,
                context.ActorId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return new FeatureDraftRecoverySnapshot(completed, null);
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
        var review = await RunVerificationAsync(context, command, cancellationToken).ConfigureAwait(false);
        return review.Release is { } release
            ? new VerifiedFeatureCandidate(review.Draft, release, review.Evidence)
            : throw Rejected(FeatureCommandRejectionReason.Precondition);
    }

    public async Task<FeatureVerificationReview> RunVerificationAsync(
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
        if (verificationReplay)
        {
            var storedVerification = draft.Verification!;
            draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
                    command.DraftId,
                    storedVerification,
                    command.ExpectedRevision,
                    command.IdempotencyId))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var evidence = storedVerification.Evidence
                ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
            DemandVerificationEvidence(evidence);
            var release = await PresentedReleaseAsync(storedVerification.Release, cancellationToken).ConfigureAwait(false);
            if (release.SourceKind != FeatureSourceKind.RuntimeAuthored ||
                !string.Equals(release.SourceReference, evidence.SourceReference, StringComparison.Ordinal) ||
                !SameSource(release.Source, draft.Source))
                throw new InvalidDataException("The persisted Feature Verification no longer matches its published release.");
            return new FeatureVerificationReview(draft, release, evidence, storedVerification.VerifiedAt);
        }
        var source = draft.Source;
        var build = await VerifyBuildAsync(
            new FeatureBuildSubmission(
                source.ImplementationProjectPath,
                source.ScenarioProjectPath,
                source.Files.Select(file => new FeatureSourceInput(file.Path, file.Content)).ToArray(),
                FeatureSourceKind.RuntimeAuthored),
            cancellationToken).ConfigureAwait(false);
        DemandVerificationEvidence(build.Evidence);
        if (build.Artifact is null)
            return new FeatureVerificationReview(draft, null, build.Evidence, timeProvider.GetUtcNow());
        var artifact = build.Artifact;
        DemandPassingArtifact(artifact);
        if (!string.Equals(artifact.Release.SourceReference, build.Evidence.SourceReference, StringComparison.Ordinal))
            throw new InvalidDataException("FeatureBuilder returned a release for another source snapshot.");
        var verification = new FeatureVerification(
            artifact.Release.Digest,
            artifact.Scenarios.Total,
            artifact.Scenarios.Passed,
            artifact.Scenarios.Failed,
            artifact.Scenarios.Skipped,
            timeProvider.GetUtcNow(),
            build.Evidence);
        var recorded = await hub.RecordVerificationAsync(new RecordFeatureVerification(
                command.DraftId,
                verification,
                command.ExpectedRevision,
                command.IdempotencyId))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return new FeatureVerificationReview(
            recorded,
            artifact.Release,
            build.Evidence,
            recorded.Verification?.VerifiedAt
                ?? throw new InvalidDataException("The recorded Feature Verification has no timestamp."));
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
            allowServerAuthoredPlan: true,
            requireServerAuthoredPlan: false,
            cancellationToken).ConfigureAwait(false);
        var inspection = reviewed.Inspection ??
            await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        var previousRelease = await PreviousInstalledReleaseAsync(
            reviewed,
            inspection,
            cancellationToken).ConfigureAwait(false);
        return new FeatureAccessReview(
            new VerifiedFeatureCandidate(reviewed.Draft, reviewed.PresentedRelease, reviewed.Draft.Verification?.Evidence),
            reviewed.InstallationId,
            reviewed.Grants,
            reviewed.Subscriptions,
            previousRelease);
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
            allowServerAuthoredPlan: false,
            requireServerAuthoredPlan: true,
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
            var replayedDraft = await Hub(context).MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
                    command.DraftId,
                    reviewed.InstallationId,
                    reviewed.Release.Digest,
                    command.ExpectedRevision,
                    command.IdempotencyId,
                    reviewed.Draft.UpdatedAt))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var replayedAuthority = await LifecycleAsync(() =>
                lifecycle.RepublishAsync(context, registration, cancellationToken)).ConfigureAwait(false);
            DemandExactAuthority(context, reviewed, replayedAuthority);
            inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
            DemandExistingCoordinate(context, reviewed, inspection);
            var replayed = DemandExactActiveInstallation(context, reviewed, inspection);
            return new InstalledFeatureVersion(replayedDraft, reviewed.PresentedRelease, replayed.Authority, registration);
        }

        var canonicalCommand = command with
        {
            Grants = reviewed.Grants,
            Subscriptions = reviewed.Subscriptions,
            RuntimeRevision = null,
            RuntimeActiveRelease = null,
            RuntimePreviousRelease = null
        };
        var hub = Hub(context);
        var existingReservation = await hub.ReadDraftInstallationReservationAsync(command.DraftId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existingReservation is not null)
        {
            canonicalCommand = canonicalCommand with
            {
                RuntimeRevision = existingReservation.RuntimeRevision,
                RuntimeActiveRelease = existingReservation.RuntimeActiveRelease,
                RuntimePreviousRelease = existingReservation.RuntimePreviousRelease
            };
        }
        else
        {
            var runtimeBaseline = inspection.Installations.SingleOrDefault(candidate =>
                candidate.Authority.InstallationId == reviewed.InstallationId &&
                candidate.Authority.ActiveRelease is not null);
            if (runtimeBaseline is not null)
            {
                var runtime = runtimeBaseline.Runtime
                    ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
                if (runtime.ActiveRelease != runtimeBaseline.Authority.ActiveRelease ||
                    runtime.PreviousRelease != runtimeBaseline.Authority.PreviousRelease)
                    throw Rejected(FeatureCommandRejectionReason.Precondition);
                canonicalCommand = canonicalCommand with
                {
                    RuntimeRevision = runtime.Revision,
                    RuntimeActiveRelease = runtime.ActiveRelease,
                    RuntimePreviousRelease = runtime.PreviousRelease
                };
            }
        }
        await hub.AcquireDraftInstallationReservationAsync(canonicalCommand, context.ActorId)
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
                new FeatureApprovalDecision(
                    approval.ApprovalId,
                    reviewed.Release.Digest,
                    true,
                    command.DecisionId,
                    context.ActorId),
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
        if (installation.Authority.PendingRelease == reviewed.Release.Digest)
        {
            DemandSameGrants(installation.Authority.PendingGrants, reviewed.Grants);
            authority = await LifecycleAsync(() =>
                lifecycle.InstallAsync(context, registration, inspection.Revision, cancellationToken)).ConfigureAwait(false);
        }
        else if (installation.Authority.ActiveRelease == reviewed.Release.Digest)
        {
            DemandSameGrants(installation.Authority.ActiveGrants, reviewed.Grants);
            DemandSameRegistration(installation.Registration, registration);
            authority = await LifecycleAsync(() =>
                lifecycle.RepublishAsync(context, registration, cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            throw Rejected(FeatureCommandRejectionReason.Precondition);
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
        return new InstalledFeatureVersion(installedDraft, reviewed.PresentedRelease, active.Authority, registration);
    }

    public async Task<InstalledFeatureDetail> ReadInstalledAsync(
        RuntimeRequestContext context,
        FeatureDraftId draftId,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(draftId);
        var draft = await ReadDraftAsync(Hub(context), draftId, cancellationToken).ConfigureAwait(false);
        var inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        return await InstalledDetailAsync(context, draft, inspection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstalledFeatureDetail> RollbackAsync(
        RuntimeRequestContext context,
        RollbackFeatureVersion command,
        CancellationToken cancellationToken = default)
    {
        FeatureSuggestionService.DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(command);
        DemandIdentifier(command.IdempotencyId, nameof(command.IdempotencyId));
        if (command.ExpectedRevision < 0)
            throw Rejected(FeatureCommandRejectionReason.Conflict);
        var draft = await ReadDraftAsync(Hub(context), command.DraftId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(draft.Status, "installed", StringComparison.Ordinal) || draft.InstallationId is not { } installationId)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        var installation = DemandInstalledCoordinate(context, draft, inspection);
        var replay = installation.Authority.RollbackReplay;
        var isReplay = replay is not null &&
                       string.Equals(replay.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal);
        if (isReplay)
        {
            if (replay!.ExpectedRevision != command.ExpectedRevision)
                throw Rejected(FeatureCommandRejectionReason.Conflict);
        }
        else
        {
            if (!installation.Authority.ExactRollbackAvailable)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            if (inspection.Revision != command.ExpectedRevision)
                throw Rejected(FeatureCommandRejectionReason.Conflict);
            if (installation.Authority.ActiveRelease != command.ExpectedActiveRelease ||
                installation.Authority.PreviousRelease != command.TargetRelease)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
        }
        await LifecycleAsync(() => lifecycle.RollbackAsync(
            context,
            new RollbackFeatureInstallation(
                installationId,
                command.ExpectedActiveRelease,
                command.TargetRelease,
                command.ExpectedRevision,
                command.IdempotencyId),
            cancellationToken)).ConfigureAwait(false);
        inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        var detail = await InstalledDetailAsync(context, draft, inspection, cancellationToken).ConfigureAwait(false);
        if (detail.ActiveRelease.Digest != command.TargetRelease || detail.PreviousRelease is not null)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        return detail;
    }

    private async Task<InstalledFeatureDetail> InstalledDetailAsync(
        RuntimeRequestContext context,
        FeatureDraft draft,
        FeatureLifecycleInspection inspection,
        CancellationToken cancellationToken)
    {
        var installation = DemandInstalledCoordinate(context, draft, inspection);
        var activeDigest = installation.Authority.ActiveRelease
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        var activeRelease = await PresentedReleaseAsync(activeDigest, cancellationToken).ConfigureAwait(false);
        FeatureReleaseMetadata? previousRelease = null;
        if (installation.Authority.ExactRollbackAvailable)
        {
            var previousDigest = installation.Authority.PreviousRelease
                ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
            previousRelease = await PresentedReleaseAsync(previousDigest, cancellationToken).ConfigureAwait(false);
        }
        return new InstalledFeatureDetail(
            draft,
            activeRelease,
            previousRelease,
            installation.Authority,
            installation.Registration!,
            inspection.Revision);
    }

    private async Task<FeatureDraftRecoverySnapshot> ReservedRecoveryAsync(
        RuntimeRequestContext context,
        FeatureDraft draft,
        FeatureDraftInstallationReservation reservation,
        CancellationToken cancellationToken)
    {
        if (reservation.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (reservation.DraftId != draft.DraftId || reservation.DraftRevision != draft.Revision ||
            reservation.Grants is not { } grants || reservation.Subscriptions is not { } subscriptions ||
            !BoundedText(reservation.InstallationId.Value, 256) ||
            !CanonicalReleaseDigest(reservation.Release) ||
            !BoundedText(reservation.DecisionId, 256) || !BoundedText(reservation.IdempotencyId, 256) ||
            grants.Any(static grant => grant is null))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var command = new InstallFeatureVersion(
            reservation.DraftId,
            reservation.DraftRevision,
            reservation.InstallationId,
            reservation.Release,
            grants,
            subscriptions,
            reservation.DecisionId,
            reservation.IdempotencyId,
            reservation.RuntimeRevision,
            reservation.RuntimeActiveRelease,
            reservation.RuntimePreviousRelease);
        if (!string.Equals(
                reservation.CommandDigest,
                FeatureInstallationReservationDigests.Command(command),
                StringComparison.Ordinal) ||
            !string.Equals(
                reservation.AccessDigest,
                FeatureInstallationReservationDigests.Access(
                    reservation.InstallationId,
                    reservation.Release,
                    grants,
                    subscriptions),
                StringComparison.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var reviewed = await ReviewAsync(
            context,
            reservation.DraftId,
            reservation.DraftRevision,
            reservation.InstallationId,
            reservation.Release,
            grants,
            subscriptions,
            allowInstalledReplay: false,
            allowServerAuthoredPlan: false,
            requireServerAuthoredPlan: true,
            cancellationToken).ConfigureAwait(false);
        if (reviewed.Draft.DraftId != draft.DraftId || reviewed.Draft.Revision != draft.Revision ||
            !grants.SequenceEqual(reviewed.Grants) ||
            !subscriptions.SequenceEqual(reviewed.Subscriptions, StringComparer.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        DemandExistingCoordinate(context, reviewed, inspection);
        var previous = await PreviousInstalledReleaseAsync(reviewed, inspection, cancellationToken).ConfigureAwait(false);
        return new FeatureDraftRecoverySnapshot(
            reviewed.Draft,
            new FeatureInstallationRecoverySnapshot(
                false,
                reviewed.Draft.Verification!,
                reviewed.Release,
                reviewed.InstallationId,
                reviewed.Grants,
                reviewed.Subscriptions,
                previous is null ? null : previous with { Source = null },
                reservation.DecisionId,
                reservation.IdempotencyId,
                false,
                false,
                null));
    }

    private async Task<FeatureDraftRecoverySnapshot> InstalledRecoveryAsync(
        RuntimeRequestContext context,
        FeatureDraft draft,
        CancellationToken cancellationToken)
    {
        if (draft.Revision <= 0)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
        var installation = DemandInstalledCoordinate(context, draft, inspection);
        if (!installation.Authority.Paused && !installation.Authority.PublicationConfirmed)
        {
            await LifecycleAsync(() => lifecycle.RepublishAsync(
                context,
                installation.Registration!,
                cancellationToken)).ConfigureAwait(false);
            inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
            installation = DemandInstalledCoordinate(context, draft, inspection);
            if (!installation.Authority.PublicationConfirmed)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
        }
        var authority = installation.Authority;
        var registration = installation.Registration!;
        var runtime = installation.Runtime!;
        var activeRelease = authority.ActiveRelease
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        DemandPauseCoordinates(authority.Paused, authority.PauseReason);
        if (runtime.Paused != authority.Paused ||
            !string.Equals(runtime.PauseReason, authority.PauseReason, StringComparison.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var reviewed = await ReviewInstalledReleaseAsync(
            context,
            registration.InstallationId,
            activeRelease,
            authority.ActiveGrants,
            registration.Subscriptions,
            cancellationToken).ConfigureAwait(false);
        if (!authority.ActiveGrants.SequenceEqual(reviewed.Grants) ||
            !registration.Subscriptions.SequenceEqual(reviewed.Subscriptions, StringComparer.Ordinal))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        FeatureReleaseMetadata? previous = null;
        if (authority.ExactRollbackAvailable)
        {
            if (authority.Paused || authority.PreviousRelease is not { } previousDigest || previousDigest == activeRelease)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            previous = await PresentedReleaseAsync(previousDigest, cancellationToken).ConfigureAwait(false);
            previous = previous with { Source = null };
        }
        return new FeatureDraftRecoverySnapshot(
            draft,
            new FeatureInstallationRecoverySnapshot(
                true,
                reviewed.Verification,
                reviewed.Release,
                registration.InstallationId,
                reviewed.Grants,
                reviewed.Subscriptions,
                previous,
                null,
                null,
                authority.ExactRollbackAvailable,
                authority.Paused,
                authority.PauseReason));
    }

    private async Task<ReviewedInstalledRelease> ReviewInstalledReleaseAsync(
        RuntimeRequestContext context,
        FeatureInstallationId installationId,
        ReleaseDigest releaseDigest,
        FeatureGrantSpec[] grants,
        string[] subscriptions,
        CancellationToken cancellationToken)
    {
        var installedDraft = await Hub(context).ReadInstalledDraftAsync(installationId, releaseDigest)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (!string.Equals(installedDraft.Status, "installed", StringComparison.Ordinal) ||
            installedDraft.InstallationId != installationId || installedDraft.Revision <= 0)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var verification = installedDraft.Verification
            ?? throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (verification.Release != releaseDigest || verification.Total <= 0 ||
            verification.Passed != verification.Total || verification.Failed != 0 || verification.Skipped != 0)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var evidence = verification.Evidence
            ?? throw new InvalidDataException("The persisted Feature Verification has no source evidence.");
        DemandVerificationEvidence(evidence);
        if (verification.Total != evidence.Total || verification.Passed != evidence.Passed ||
            verification.Failed != evidence.Failed || verification.Skipped != evidence.Skipped)
            throw new InvalidDataException("The persisted Feature Verification has inconsistent evidence totals.");
        var presentedRelease = await PresentedReleaseAsync(releaseDigest, cancellationToken).ConfigureAwait(false);
        if (presentedRelease.SourceKind != FeatureSourceKind.RuntimeAuthored ||
            !string.Equals(presentedRelease.SourceReference, evidence.SourceReference, StringComparison.Ordinal) ||
            !SameSource(presentedRelease.Source, installedDraft.Source))
            throw new InvalidDataException("The installed Feature release does not match its verified source evidence.");
        var release = presentedRelease with { Source = null };
        return new ReviewedInstalledRelease(
            verification,
            release,
            ValidateGrants(release, grants),
            ValidateSubscriptions(subscriptions));
    }

    private static FeatureInstallationInspection DemandInstalledCoordinate(
        RuntimeRequestContext context,
        FeatureDraft draft,
        FeatureLifecycleInspection inspection)
    {
        if (!string.Equals(draft.Status, "installed", StringComparison.Ordinal) || draft.InstallationId is not { } installationId)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var matches = inspection.Installations.Where(candidate =>
            candidate.Authority.InstallationId == installationId).ToArray();
        if (matches.Length != 1)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var installation = matches[0];
        if (installation.Authority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (installation.Authority.ActiveRelease is not { } activeRelease ||
            installation.Registration is not { } registration ||
            registration.InstallationId != installationId || registration.Release != activeRelease ||
            installation.Runtime is not { } runtime || runtime.InstallationId != installationId ||
            runtime.ActiveRelease != activeRelease)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        return installation;
    }

    private async Task<FeatureReleaseMetadata?> PreviousInstalledReleaseAsync(
        ReviewedInstallation reviewed,
        FeatureLifecycleInspection inspection,
        CancellationToken cancellationToken)
    {
        var installation = inspection.Installations.SingleOrDefault(candidate =>
            candidate.Authority.InstallationId == reviewed.InstallationId);
        if (installation is null) return null;
        var digest = installation.Authority.ActiveRelease != reviewed.Release.Digest
            ? installation.Authority.ActiveRelease
            : installation.Authority.PreviousRelease;
        return digest is { } previousDigest
            ? await PresentedReleaseAsync(previousDigest, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private async Task<FeatureReleaseMetadata> PresentedReleaseAsync(
        ReleaseDigest digest,
        CancellationToken cancellationToken)
    {
        var published = await ArtifactAsync(() =>
            artifacts.DemandReleaseAsync(digest, cancellationToken)).ConfigureAwait(false);
        if (published.Digest != digest)
            throw new InvalidDataException("The published Feature release has another digest.");
        var source = await ArtifactAsync(() =>
            artifacts.DemandSourceAsync(published.SourceReference, cancellationToken)).ConfigureAwait(false);
        if (published.Source is { } embedded && !SameSource(embedded, source))
            throw new InvalidDataException("The published Feature release contains conflicting source snapshots.");
        return published with { Source = source };
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

    private static void DemandVerificationEvidence(FeatureVerificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.Scenarios);
        ArgumentNullException.ThrowIfNull(evidence.Artifacts);
        if (!CanonicalSourceReference(evidence.SourceReference) ||
            evidence.Total is <= 0 or > 1024 ||
            evidence.Passed < 0 || evidence.Failed < 0 || evidence.Skipped < 0 ||
            (long)evidence.Passed + evidence.Failed + evidence.Skipped != evidence.Total ||
            evidence.Scenarios.Length != evidence.Total)
            throw new InvalidDataException("FeatureBuilder returned inconsistent verification evidence.");
        long utf8Bytes = Encoding.UTF8.GetByteCount(evidence.SourceReference);
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var scenario in evidence.Scenarios)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            if (!BoundedText(scenario.ScenarioId, 256) || !BoundedText(scenario.Name, 512) ||
                !scenarioIds.Add(scenario.ScenarioId) || scenario.DurationMilliseconds is < 0 or > 70_000)
                throw new InvalidDataException("FeatureBuilder returned invalid scenario evidence.");
            switch (scenario.Outcome)
            {
                case FeatureScenarioOutcome.Passed:
                    passed++;
                    if (scenario.SafeFailure is not null)
                        throw new InvalidDataException("Passing scenario evidence cannot contain a failure.");
                    break;
                case FeatureScenarioOutcome.Failed:
                    failed++;
                    if (!BoundedText(scenario.SafeFailure, 4096))
                        throw new InvalidDataException("Failed scenario evidence requires a safe bounded failure.");
                    break;
                case FeatureScenarioOutcome.Skipped:
                    skipped++;
                    if (scenario.SafeFailure is { } skippedReason && !BoundedText(skippedReason, 4096))
                        throw new InvalidDataException("Skipped scenario evidence contains an invalid reason.");
                    break;
                default:
                    throw new InvalidDataException("FeatureBuilder returned an unknown scenario outcome.");
            }
            utf8Bytes = checked(utf8Bytes +
                Encoding.UTF8.GetByteCount(scenario.ScenarioId) +
                Encoding.UTF8.GetByteCount(scenario.Name) +
                Encoding.UTF8.GetByteCount(scenario.SafeFailure ?? string.Empty));
        }
        if (passed != evidence.Passed || failed != evidence.Failed || skipped != evidence.Skipped)
            throw new InvalidDataException("FeatureBuilder returned inconsistent scenario totals.");
        var artifactNames = new HashSet<string>(StringComparer.Ordinal);
        if (evidence.Artifacts.Length > 32)
            throw new InvalidDataException("FeatureBuilder returned too many verification artifacts.");
        foreach (var artifact in evidence.Artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            if (!BoundedText(artifact.Name, 256) || !artifactNames.Add(artifact.Name) ||
                !BoundedText(artifact.MediaType, 128) || artifact.SizeBytes is < 0 or > 1_048_576 ||
                !CanonicalSourceReference(artifact.Digest))
                throw new InvalidDataException("FeatureBuilder returned invalid verification artifact evidence.");
            utf8Bytes = checked(utf8Bytes +
                Encoding.UTF8.GetByteCount(artifact.Name) +
                Encoding.UTF8.GetByteCount(artifact.MediaType) +
                Encoding.UTF8.GetByteCount(artifact.Digest));
        }
        if (utf8Bytes > MaximumVerificationEvidenceUtf8Bytes)
            throw new InvalidDataException("FeatureBuilder returned verification evidence exceeding its UTF-8 byte budget.");
    }

    private static bool CanonicalSourceReference(string value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        !value.Skip(7).Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'));

    private static bool BoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) && !value.Any(char.IsControl);

    private static bool CanonicalReleaseDigest(ReleaseDigest digest) =>
        digest.Value is { Length: 64 } &&
        !digest.Value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'));

    private static void DemandPauseCoordinates(bool paused, string? pauseReason)
    {
        if (paused != (pauseReason is not null) || pauseReason is not null && !BoundedText(pauseReason, 4096))
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
        bool allowServerAuthoredPlan,
        bool requireServerAuthoredPlan,
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
        var evidence = verification.Evidence
            ?? throw new InvalidDataException("The persisted Feature Verification has no source evidence.");
        DemandVerificationEvidence(evidence);
        if (verification.Total != evidence.Total || verification.Passed != evidence.Passed ||
            verification.Failed != evidence.Failed || verification.Skipped != evidence.Skipped)
            throw new InvalidDataException("The persisted Feature Verification has inconsistent evidence totals.");
        var publishedRelease = await ArtifactAsync(() =>
            artifacts.DemandReleaseAsync(releaseDigest, cancellationToken)).ConfigureAwait(false);
        if (publishedRelease.Digest != releaseDigest || publishedRelease.SourceKind != FeatureSourceKind.RuntimeAuthored)
            throw new InvalidDataException("The published Feature release does not match the verified runtime-authored digest.");
        if (!string.Equals(publishedRelease.SourceReference, evidence.SourceReference, StringComparison.Ordinal))
            throw new InvalidDataException("The published Feature release source does not match the verified source evidence.");
        var source = await ArtifactAsync(() =>
            artifacts.DemandSourceAsync(publishedRelease.SourceReference, cancellationToken)).ConfigureAwait(false);
        if (publishedRelease.Source is { } embeddedSource && !SameSource(embeddedSource, source))
            throw new InvalidDataException("The published Feature release contains conflicting source snapshots.");
        var release = publishedRelease with { Source = null };
        var presentedRelease = release with { Source = source };
        FeatureLifecycleInspection? inspection = null;
        FeatureGrantSpec[] reviewedGrants;
        string[] reviewedSubscriptions;
        if ((allowServerAuthoredPlan && grants.Length == 0 && subscriptions.Length == 0) ||
            requireServerAuthoredPlan)
        {
            inspection = await LifecycleAsync(() => lifecycle.InspectAsync(context, cancellationToken)).ConfigureAwait(false);
            var plan = await ServerAuthoredPlanAsync(
                context,
                installationId,
                release,
                inspection,
                cancellationToken).ConfigureAwait(false);
            var canonicalGrants = ValidateGrants(release, plan.Grants);
            var canonicalSubscriptions = ValidateSubscriptions(plan.Subscriptions);
            if (requireServerAuthoredPlan)
            {
                reviewedGrants = ValidateGrants(release, grants);
                reviewedSubscriptions = ValidateSubscriptions(subscriptions);
                DemandSameGrants(reviewedGrants, canonicalGrants);
                if (!reviewedSubscriptions.SequenceEqual(canonicalSubscriptions, StringComparer.Ordinal))
                    throw Rejected(FeatureCommandRejectionReason.Precondition);
            }
            reviewedGrants = canonicalGrants;
            reviewedSubscriptions = canonicalSubscriptions;
        }
        else
        {
            reviewedGrants = ValidateGrants(release, grants);
            reviewedSubscriptions = ValidateSubscriptions(subscriptions);
        }
        return new ReviewedInstallation(
            draft,
            release,
            presentedRelease,
            installationId,
            reviewedGrants,
            reviewedSubscriptions,
            installedReplay,
            inspection);
    }

    private async Task<AuthorityPlan> ServerAuthoredPlanAsync(
        RuntimeRequestContext context,
        FeatureInstallationId installationId,
        FeatureReleaseMetadata release,
        FeatureLifecycleInspection inspection,
        CancellationToken cancellationToken)
    {
        var requested = release.RequestedCapabilities;
        if (requested is null || requested.Length > 32 ||
            requested.Any(capabilityId => !BoundedText(capabilityId, 256)) ||
            requested.Distinct(StringComparer.Ordinal).Count() != requested.Length)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var installations = inspection.Installations.Where(candidate =>
            candidate.Authority.InstallationId == installationId).ToArray();
        if (installations.Length > 1)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (installations.Length == 1)
        {
            var installation = installations[0];
            if (installation.Authority.ActorId != context.ActorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            if (installation.Authority.Paused)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            var activeRelease = installation.Authority.ActiveRelease;
            var registration = installation.Registration;
            if (installation.Authority.PendingRelease is { } pendingRelease && pendingRelease != release.Digest)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            if (activeRelease is null)
            {
                if (installation.Authority.ActiveGrantRevision is not null ||
                    installation.Authority.ActiveGrants.Length != 0 || registration is not null ||
                    installation.Authority.PendingRelease != release.Digest ||
                    installation.Authority.PendingGrantRevision is null)
                    throw Rejected(FeatureCommandRejectionReason.Precondition);
                var pendingPlan = await CatalogGrantsAsync(requested, cancellationToken).ConfigureAwait(false);
                return new AuthorityPlan(pendingPlan, ["manual"]);
            }
            if (installation.Authority.ActiveGrantRevision is null ||
                registration is null || registration.InstallationId != installationId ||
                registration.Release != activeRelease)
                throw Rejected(FeatureCommandRejectionReason.Precondition);

            var activeGrants = installation.Authority.ActiveGrants;
            if (activeGrants is null ||
                activeGrants.Any(grant => grant is null) ||
                activeGrants.Select(grant => grant.CapabilityId).Distinct(StringComparer.Ordinal).Count() != activeGrants.Length)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            var activeByCapability = activeGrants.ToDictionary(grant => grant.CapabilityId, StringComparer.Ordinal);
            var retained = requested
                .Where(activeByCapability.ContainsKey)
                .Select(capabilityId => activeByCapability[capabilityId])
                .ToArray();
            var additions = await CatalogGrantsAsync(
                requested.Where(capabilityId => !activeByCapability.ContainsKey(capabilityId)).ToArray(),
                cancellationToken).ConfigureAwait(false);
            return new AuthorityPlan(
                [.. retained, .. additions],
                registration.Subscriptions.ToArray());
        }
        var existingApprovals = inspection.Approvals.Where(candidate =>
            candidate.InstallationId == installationId &&
            candidate.Status != FeatureApprovalStatus.Superseded).ToArray();
        if (inspection.Registrations.Any(candidate => candidate.InstallationId == installationId) ||
            existingApprovals.Length > 1 ||
            existingApprovals.Any(candidate => candidate.Release.Digest != release.Digest))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var grants = await CatalogGrantsAsync(requested, cancellationToken).ConfigureAwait(false);
        return new AuthorityPlan(grants, ["manual"]);
    }

    private async Task<FeatureGrantSpec[]> CatalogGrantsAsync(
        string[] requested,
        CancellationToken cancellationToken)
    {
        if (requested.Length == 0)
            return [];
        var catalog = await ReadCapabilityCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (catalog.Count is 0 or > 256 ||
            catalog.Any(descriptor => descriptor is null) ||
            catalog.Select(descriptor => descriptor.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Count)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var descriptors = catalog.ToDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);
        var grants = new FeatureGrantSpec[requested.Length];
        for (var index = 0; index < requested.Length; index++)
        {
            var capabilityId = requested[index];
            if (!descriptors.TryGetValue(capabilityId, out var descriptor) ||
                !descriptor.Available || descriptor.Version < 1 ||
                descriptor.RequiredConnections is null ||
                descriptor.RequiredConnections.Any(connectionId => !BoundedText(connectionId, 64)) ||
                descriptor.RequiredConnections.Distinct(StringComparer.Ordinal).Count() != descriptor.RequiredConnections.Length ||
                descriptor.RequiredConnections.Length > 1 ||
                descriptor.RequiredGrants is null || descriptor.RequiredGrants.Length > 32 ||
                descriptor.RequiredGrants.Any(toolId => !BoundedText(toolId, 256)) ||
                descriptor.RequiredGrants.Distinct(StringComparer.Ordinal).Count() != descriptor.RequiredGrants.Length)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            var connection = descriptor.RequiredConnections.SingleOrDefault();
            var allowedToolIds = new[] { capabilityId }
                .Concat(descriptor.RequiredGrants
                    .Where(toolId => !string.Equals(toolId, capabilityId, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal))
                .ToArray();
            grants[index] = new FeatureGrantSpec(
                capabilityId,
                descriptor.Version,
                connection is null ? null : new ProviderConnectionId(connection),
                JsonSerializer.Serialize(new { allowedToolIds }),
                connection);
        }
        return grants;
    }

    private async Task<IReadOnlyList<CapabilityDescriptor>> ReadCapabilityCatalogAsync(
        CancellationToken cancellationToken)
    {
        if (capabilityCatalog is null)
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        try
        {
            return await capabilityCatalog.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureCommandRejectedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or
                                          KeyNotFoundException or IOException or TimeoutException or OrleansException)
        {
            throw Rejected(FeatureCommandRejectionReason.Unavailable);
        }
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
            candidate.InstallationId == reviewed.InstallationId &&
            candidate.Status != FeatureApprovalStatus.Superseded).ToArray();
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
                candidate.Status != FeatureApprovalStatus.Superseded &&
                candidate.Release.Digest == reviewed.Release.Digest && candidate.InstallationId != reviewed.InstallationId) ||
            inspection.Installations.Any(candidate =>
                candidate.Authority.InstallationId != reviewed.InstallationId &&
                (candidate.Authority.ActiveRelease == reviewed.Release.Digest || candidate.Authority.PendingRelease == reviewed.Release.Digest)) ||
            inspection.Registrations.Any(candidate =>
                candidate.Release == reviewed.Release.Digest && candidate.InstallationId != reviewed.InstallationId))
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        if (registrations.Length == 1)
        {
            if (registrations[0].Release == reviewed.Release.Digest)
            {
                var hasExactPendingRegistration = matching.Length == 1 &&
                    matching[0].Authority.PendingRelease == reviewed.Release.Digest;
                if (!hasExactPendingRegistration)
                    DemandSameRegistration(
                        registrations[0],
                        new FeatureInstallationRegistration(reviewed.InstallationId, reviewed.Release.Digest, reviewed.Subscriptions));
            }
            else if (matching.Length != 1 || matching[0].Authority.ActiveRelease != registrations[0].Release)
            {
                throw Rejected(FeatureCommandRejectionReason.Precondition);
            }
        }
        if (matching.Length == 0) return;
        var installation = matching[0];
        if (installation.Authority.ActorId != context.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (installation.Authority.PendingRelease is { } pendingRelease && pendingRelease != reviewed.Release.Digest)
            throw Rejected(FeatureCommandRejectionReason.Precondition);
        var hasExactPending = installation.Authority.PendingRelease == reviewed.Release.Digest;
        if (hasExactPending)
            DemandSameGrants(installation.Authority.PendingGrants, reviewed.Grants);
        if (installation.Authority.ActiveRelease == reviewed.Release.Digest && !hasExactPending)
        {
            DemandSameGrants(installation.Authority.ActiveGrants, reviewed.Grants);
            DemandSameRegistration(
                installation.Registration,
                new FeatureInstallationRegistration(reviewed.InstallationId, reviewed.Release.Digest, reviewed.Subscriptions));
        }
        else if (installation.Authority.ActiveRelease is { } existingActive)
        {
            if (installation.Registration is not { } existingRegistration ||
                existingRegistration.InstallationId != reviewed.InstallationId ||
                existingRegistration.Release != existingActive)
                throw Rejected(FeatureCommandRejectionReason.Precondition);
        }
    }

    private static FeatureApprovalSnapshot? ExactApproval(ReviewedInstallation reviewed, FeatureLifecycleInspection inspection)
    {
        var approval = inspection.Approvals.SingleOrDefault(candidate =>
            candidate.InstallationId == reviewed.InstallationId && candidate.Release.Digest == reviewed.Release.Digest &&
            candidate.Status != FeatureApprovalStatus.Superseded);
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
        left.Dependencies.SequenceEqual(right.Dependencies, StringComparer.Ordinal) &&
        SameSource(left.Source, right.Source);

    private static bool SameSource(FeatureSourceSnapshot? left, FeatureSourceSnapshot? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        string.Equals(left.ImplementationProjectPath, right.ImplementationProjectPath, StringComparison.Ordinal) &&
        string.Equals(left.ScenarioProjectPath, right.ScenarioProjectPath, StringComparison.Ordinal) &&
        left.Files.SequenceEqual(right.Files);

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

    private async Task<FeatureBuildReview> VerifyBuildAsync(
        FeatureBuildSubmission submission,
        CancellationToken cancellationToken)
    {
        try
        {
            return await builds.VerifyAsync(submission, cancellationToken).ConfigureAwait(false);
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
        FeatureReleaseMetadata PresentedRelease,
        FeatureInstallationId InstallationId,
        FeatureGrantSpec[] Grants,
        string[] Subscriptions,
        bool InstalledReplay,
        FeatureLifecycleInspection? Inspection);

    private sealed record ReviewedInstalledRelease(
        FeatureVerification Verification,
        FeatureReleaseMetadata Release,
        FeatureGrantSpec[] Grants,
        string[] Subscriptions);

    private sealed record AuthorityPlan(FeatureGrantSpec[] Grants, string[] Subscriptions);
}
