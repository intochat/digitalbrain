using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Shared;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureDraftAuthoringTransitions
{
    private static readonly char[] InvalidSourcePathCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> ReservedSourcePathSegments = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"],
        StringComparer.OrdinalIgnoreCase);

    public static FeatureDraft? ReadDraft(FeatureHubState state, FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandDraftId(draftId);
        return (state.Drafts ?? []).FirstOrDefault(draft => draft.DraftId == draftId);
    }

    public static FeatureDraft? ReadInstalledDraft(
        FeatureHubState state,
        FeatureInstallationId installationId,
        ReleaseDigest release)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandText(installationId.Value, 256, nameof(installationId));
        DemandRelease(release);
        var matches = (state.Drafts ?? []).Where(draft =>
            string.Equals(draft.Status, "installed", StringComparison.Ordinal) &&
            draft.InstallationId == installationId &&
            draft.Verification?.Release == release).ToArray();
        if (matches.Length > 1)
            throw new FeatureConcurrencyException(
                "The installed Feature release is bound to multiple Drafts.",
                FeatureCommandRejectionReason.Precondition);
        return matches.SingleOrDefault();
    }

    public static FeatureDraftInstallationReservation? ReadInstallationReservation(FeatureHubState state, FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandDraftId(draftId);
        return (state.DraftInstallationReservations ?? []).SingleOrDefault(candidate => candidate.DraftId == draftId);
    }

    public static FeatureDraftInstallationResetObligation? ReadInstallationReset(FeatureHubState state, FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandDraftId(draftId);
        var reset = (state.DraftInstallationResets ?? []).SingleOrDefault(candidate => candidate.DraftId == draftId);
        return reset is null
            ? null
            : new FeatureDraftInstallationResetObligation(
                reset.DraftId,
                reset.IdempotencyId,
                reset.ActorId,
                reset.InstallationId,
                reset.Release,
                reset.RequiresRepublish);
    }

    public static FeatureDraftInstallationReservationTransition AcquireInstallationReservation(
        FeatureHubState state,
        InstallFeatureVersion command,
        ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        var canonicalCommand = CanonicalInstallation(command);
        DemandText(actorId.Value, 256, nameof(actorId));
        if ((state.DraftInstallationResets ?? []).Any(candidate => candidate.DraftId == command.DraftId))
            throw new FeatureConcurrencyException(
                "The Feature Draft has an installation reset in progress.",
                FeatureCommandRejectionReason.Precondition);
        var commandDigest = FeatureInstallationReservationDigests.Command(canonicalCommand);
        var accessDigest = FeatureInstallationReservationDigests.Access(
            command.InstallationId,
            command.Release,
            canonicalCommand.Grants,
            canonicalCommand.Subscriptions);
        var reservations = (state.DraftInstallationReservations ?? []).ToArray();
        if ((state.Drafts ?? []).Any(candidate =>
                candidate.DraftId != command.DraftId &&
                candidate.InstallationId is not null &&
                candidate.Verification?.Release == command.Release))
            throw new FeatureConcurrencyException("The Feature installation coordinate is already bound to another Draft.");
        var existing = reservations.SingleOrDefault(candidate => candidate.DraftId == command.DraftId);
        if (existing is not null)
        {
            if (existing.DraftRevision != command.ExpectedRevision ||
                existing.InstallationId != command.InstallationId ||
                existing.Release != command.Release ||
                existing.ActorId != actorId ||
                existing.RuntimeRevision != canonicalCommand.RuntimeRevision ||
                existing.RuntimeActiveRelease != canonicalCommand.RuntimeActiveRelease ||
                existing.RuntimePreviousRelease != canonicalCommand.RuntimePreviousRelease ||
                !string.Equals(existing.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal) ||
                !string.Equals(existing.CommandDigest, commandDigest, StringComparison.Ordinal) ||
                !string.Equals(existing.AccessDigest, accessDigest, StringComparison.Ordinal) ||
                !string.Equals(existing.DecisionId, command.DecisionId, StringComparison.Ordinal) ||
                existing.Grants is null || !existing.Grants.SequenceEqual(canonicalCommand.Grants) ||
                existing.Subscriptions is null ||
                !existing.Subscriptions.SequenceEqual(canonicalCommand.Subscriptions, StringComparer.Ordinal))
                throw new FeatureConcurrencyException("The Feature Draft is reserved for a different installation command.");
            DemandInstallationLedgerBudget(
                reservations,
                state.DraftInstallationResets ?? []);
            return new FeatureDraftInstallationReservationTransition(state, existing);
        }
        FeatureHubEvidenceLedger.DemandOwnerCoordinateCapacity(state, command.InstallationId);
        var authorityBaseline = state.Authorities.SingleOrDefault(candidate => candidate.InstallationId == command.InstallationId);
        if (authorityBaseline?.ActiveRelease is { } activeBaseline)
        {
            if (canonicalCommand.RuntimeRevision is null || canonicalCommand.RuntimeActiveRelease != activeBaseline ||
                canonicalCommand.RuntimePreviousRelease != authorityBaseline.PreviousRelease)
                throw new FeatureConcurrencyException(
                    "The Feature installation runtime baseline does not match the active authority.",
                    FeatureCommandRejectionReason.Precondition);
        }
        else if (canonicalCommand.RuntimeRevision is not null || canonicalCommand.RuntimeActiveRelease is not null ||
                 canonicalCommand.RuntimePreviousRelease is not null)
        {
            throw new FeatureConcurrencyException(
                "A new Feature installation cannot bind an existing runtime baseline.",
                FeatureCommandRejectionReason.Precondition);
        }
        FeatureInstallationAuthorityBaseline? capturedAuthorityBaseline = null;
        if (authorityBaseline?.ActiveRelease is not null)
        {
            var activeRelease = authorityBaseline.ActiveRelease.Value;
            if (authorityBaseline.ActorId != actorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            if (authorityBaseline.PendingRelease is not null || authorityBaseline.PendingGrantRevision is not null ||
                authorityBaseline.PendingGrants.Length != 0)
                throw new FeatureConcurrencyException(
                    "The active Feature authority already has a pending candidate.",
                    FeatureCommandRejectionReason.Precondition);
            var registrations = state.Installations.Where(candidate => candidate.InstallationId == command.InstallationId).ToArray();
            if (registrations.Length != 1 || registrations[0].Release != authorityBaseline.ActiveRelease)
                throw new FeatureConcurrencyException(
                    "The active Feature registration is unavailable for reservation.",
                    FeatureCommandRejectionReason.Precondition);
            if (activeRelease == canonicalCommand.Release)
            {
                var activeAccessDigest = FeaturePublicationTransitions.AccessDigest(
                    command.InstallationId,
                    activeRelease,
                    authorityBaseline.ActiveGrants,
                    registrations[0].Subscriptions);
                if (!string.Equals(activeAccessDigest, accessDigest, StringComparison.Ordinal))
                    throw new FeatureConcurrencyException(
                        "A same-release installation cannot change its reviewed access plan.",
                        FeatureCommandRejectionReason.Precondition);
            }
            capturedAuthorityBaseline = AuthorityBaseline(authorityBaseline, registrations[0]);
        }
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        var verification = draft.Verification
            ?? throw new FeatureConcurrencyException(
                "The Feature Draft has no Verification to reserve for installation.",
                FeatureCommandRejectionReason.Precondition);
        if (verification.Release != command.Release || verification.Total <= 0 ||
            verification.Passed != verification.Total || verification.Failed != 0 || verification.Skipped != 0)
            throw new FeatureConcurrencyException(
                "Only the exact fully verified Feature release can be reserved for installation.",
                FeatureCommandRejectionReason.Precondition);
        if (draft.InstallationId is { } installationId && installationId != command.InstallationId)
            throw new FeatureConcurrencyException(
                "The Feature Draft is bound to another installation identity.",
                FeatureCommandRejectionReason.Precondition);
        if (reservations.Any(candidate =>
                candidate.InstallationId == command.InstallationId ||
                candidate.Release == command.Release && candidate.InstallationId != command.InstallationId))
            throw new FeatureConcurrencyException("The Feature installation coordinate is already reserved.");
        if (reservations.Length >= FeatureLimits.DraftInstallationReservations)
            throw new FeatureLimitExceededException("An Owner can have at most 100 active Feature installation reservations.");
        var reservation = new FeatureDraftInstallationReservation(
            command.DraftId,
            command.ExpectedRevision,
            command.InstallationId,
            command.Release,
            command.IdempotencyId,
            commandDigest,
            accessDigest,
            command.DecisionId,
            actorId,
            canonicalCommand.Grants,
            canonicalCommand.Subscriptions,
            canonicalCommand.RuntimeRevision,
            canonicalCommand.RuntimeActiveRelease,
            canonicalCommand.RuntimePreviousRelease,
            capturedAuthorityBaseline);
        DemandInstallationLedgerBudget(
            [.. reservations, reservation],
            state.DraftInstallationResets ?? []);
        return new FeatureDraftInstallationReservationTransition(
            state with
            {
                DraftInstallationReservations = [.. reservations, reservation],
                Revision = NextRevision(state.Revision)
            },
            reservation);
    }

    public static FeatureDraftInstallationResetTransition ResetInstallationReservation(
        FeatureHubState state,
        ResetFeatureDraftInstallationReservation command,
        ActorId actorId,
        DateTimeOffset resetAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandText(command.IdempotencyId, 256, nameof(command.IdempotencyId));
        DemandText(actorId.Value, 256, nameof(actorId));
        if (resetAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature Draft reset timestamps must be UTC.", nameof(resetAt));
        var priorReplay = (state.DraftReplays ?? []).FirstOrDefault(candidate =>
            candidate.DraftId == command.DraftId &&
            string.Equals(candidate.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal));
        if (priorReplay is not null)
        {
            if ((state.DraftInstallationReservations ?? []).Any(candidate => candidate.DraftId == command.DraftId))
                throw new FeatureConcurrencyException("A new Feature installation reservation supersedes this reset replay.");
            if (!string.Equals(priorReplay.Kind, "installation-reset", StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The idempotency identifier is already bound to a different authoring command.");
            if (priorReplay.ActorId != actorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            if (command.ReservedInstallation is { } replayedInstallation)
            {
                var canonicalReplay = CanonicalInstallation(replayedInstallation);
                if (canonicalReplay.DraftId != command.DraftId ||
                    !string.Equals(priorReplay.PayloadDigest, ResetFingerprint(command.IdempotencyId, actorId, canonicalReplay), StringComparison.Ordinal))
                    throw new FeatureConcurrencyException("The reset idempotency identifier is bound to another installation reservation.");
            }
            var current = ReadDraft(state, command.DraftId)
                ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
            return new FeatureDraftInstallationResetTransition(state, priorReplay.Result(current), true, false, false, false, null, true);
        }

        var reservation = ReadInstallationReservation(state, command.DraftId);
        if (reservation is null)
        {
            var current = ReadDraft(state, command.DraftId)
                ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
            var completedReset = (state.DraftReplays ?? [])
                .Where(candidate => candidate.DraftId == command.DraftId &&
                    string.Equals(candidate.Kind, "installation-reset", StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.ResultRevision)
                .ThenByDescending(candidate => candidate.ResultUpdatedAt)
                .ThenBy(candidate => candidate.IdempotencyId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (completedReset is null ||
                !string.Equals(Fingerprint(completedReset.Result(current)), Fingerprint(current), StringComparison.Ordinal))
                throw new FeatureConcurrencyException(
                    "The Feature Draft has no active installation reservation to reset.",
                    FeatureCommandRejectionReason.Precondition);
            if (completedReset.ActorId != actorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            return new FeatureDraftInstallationResetTransition(
                state,
                current,
                true,
                false,
                false,
                false,
                null,
                true);
        }
        if (reservation.ActorId != actorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        var draft = DemandEditableDraft(state, command.DraftId, reservation.DraftRevision, allowReserved: true);
        var legacyPlan = reservation.Grants is null && reservation.Subscriptions is null;
        if (reservation.Grants is null != (reservation.Subscriptions is null))
            throw new FeatureConcurrencyException(
                "The Feature Draft installation reservation has an incomplete access plan.",
                FeatureCommandRejectionReason.Precondition);

        InstallFeatureVersion? canonicalInstallation = null;
        FeatureGrantState[] reservationGrants = [];
        string[] reservationSubscriptions = [];
        string payloadDigest;
        if (legacyPlan)
        {
            if (command.ReservedInstallation is not null)
                throw new FeatureConcurrencyException("A legacy Feature installation reservation cannot be rebound to a reconstructed access plan.");
            ValidateLegacyReservation(reservation);
            payloadDigest = ResetFingerprint(command.IdempotencyId, actorId, reservation);
        }
        else
        {
            canonicalInstallation = CanonicalInstallation(command.ReservedInstallation
                ?? throw new FeatureConcurrencyException(
                    "The exact reserved installation command is required.",
                    FeatureCommandRejectionReason.Precondition));
            if (canonicalInstallation.DraftId != command.DraftId)
                throw new FeatureConcurrencyException("The reset command targets another Feature Draft.");
            DemandExactReservation(reservation, canonicalInstallation, actorId);
            reservationGrants = FeatureHubTransitions.ValidateGrants(canonicalInstallation.Grants);
            reservationSubscriptions = canonicalInstallation.Subscriptions;
            payloadDigest = ResetFingerprint(command.IdempotencyId, actorId, canonicalInstallation);
        }

        var inProgress = (state.DraftInstallationResets ?? []).SingleOrDefault(candidate => candidate.DraftId == command.DraftId);
        if (inProgress is not null)
        {
            if (!string.Equals(inProgress.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal) ||
                inProgress.ActorId != actorId || inProgress.InstallationId != reservation.InstallationId ||
                inProgress.Release != reservation.Release ||
                !string.Equals(inProgress.CommandDigest, reservation.CommandDigest, StringComparison.Ordinal) ||
                !inProgress.RequiresRepublish)
                throw new FeatureConcurrencyException("The Feature Draft has another installation reset in progress.");
            var preserved = DemandExactPreparedReset(
                state,
                inProgress,
                reservation,
                actorId,
                requireConfirmed: false);
            return new FeatureDraftInstallationResetTransition(
                state,
                draft,
                false,
                false,
                false,
                true,
                preserved,
                false);
        }
        if ((state.DraftInstallationResets ?? []).Any(candidate => candidate.DraftId == command.DraftId))
            throw new FeatureConcurrencyException("The Feature Draft has an ambiguous installation reset state.");

        var approvals = state.Approvals.ToArray();
        var coordinateApprovals = approvals
            .Select((approval, index) => (approval, index))
            .Where(candidate =>
                candidate.approval.InstallationId == reservation.InstallationId &&
                candidate.approval.Release.Digest == reservation.Release &&
                candidate.approval.Status != FeatureApprovalStatus.Superseded)
            .ToArray();
        if (legacyPlan && coordinateApprovals.Length != 0)
            throw new FeatureConcurrencyException("A legacy reservation with lifecycle authority cannot be reset safely.");
        if (coordinateApprovals.Length > 1)
            throw new FeatureConcurrencyException("The Feature installation reservation has ambiguous current approvals.");
        var nextRevision = NextRevision(state.Revision);
        if (coordinateApprovals.Length == 1)
        {
            var (approval, index) = coordinateApprovals[0];
            if (!FeatureHubTransitions.SameGrants(approval.Grants, reservationGrants) ||
                !FeatureHubTransitions.SameRelease(
                    approval.Release,
                    state.Releases.SingleOrDefault(candidate => candidate.Digest == reservation.Release)
                    ?? throw new FeatureConcurrencyException("The reserved Feature release metadata is unavailable.")))
                throw new FeatureConcurrencyException("The current approval does not match the exact reserved access plan.");
            if (approval.Status == FeatureApprovalStatus.Pending)
            {
                if (approval.DecisionId is not null || approval.DecidedAt is not null || approval.DecisionActorId is not null)
                    throw new FeatureConcurrencyException("The pending approval has an invalid decision binding.");
            }
            else if (approval.Status == FeatureApprovalStatus.Approved)
            {
                if (!string.Equals(approval.DecisionId, reservation.DecisionId, StringComparison.Ordinal) ||
                    approval.DecidedAt is null || approval.DecisionActorId != reservation.ActorId)
                    throw new FeatureConcurrencyException("The approval decision does not match the exact reservation.");
            }
            else if (approval.Status == FeatureApprovalStatus.Rejected)
            {
                if (approval.DecisionId is null || approval.DecidedAt is null ||
                    approval.DecisionActorId != reservation.ActorId)
                    throw new FeatureConcurrencyException("The rejected approval has no durable decision binding.");
                DemandText(approval.DecisionId, 256, nameof(approval.DecisionId));
            }
            else
            {
                throw new FeatureConcurrencyException("The current approval cannot be safely superseded.");
            }
            approvals[index] = approval with { Status = FeatureApprovalStatus.Superseded, Revision = nextRevision };
            approvals = FeatureApprovalLedger.Compact(approvals);
        }

        var registrations = state.Installations.Where(candidate => candidate.InstallationId == reservation.InstallationId).ToArray();
        var authorityIndexes = state.Authorities
            .Select((authority, index) => (authority, index))
            .Where(candidate => candidate.authority.InstallationId == reservation.InstallationId)
            .ToArray();
        if (legacyPlan && (registrations.Length != 0 || authorityIndexes.Length != 0))
            throw new FeatureConcurrencyException("A legacy reservation with lifecycle authority cannot be reset safely.");
        if (registrations.Length > 1 || authorityIndexes.Length > 1)
            throw new FeatureConcurrencyException("The Feature installation coordinate is ambiguous.");

        var authorities = state.Authorities.ToList();
        var restoredInstallations = state.Installations.ToArray();
        var requiresDiscard = false;
        var requiresRuntimeAbsence = legacyPlan;
        var requiresRepublish = false;
        FeatureInstallationAuthorityState? preservedAuthority = null;
        if (!legacyPlan && authorityIndexes.Length == 0)
        {
            if (registrations.Length != 0)
                throw new FeatureConcurrencyException("An uncommitted Feature installation cannot have a registration.");
            requiresDiscard = true;
        }
        else if (!legacyPlan)
        {
            var (authority, authorityIndex) = authorityIndexes[0];
            if (authority.ActorId != actorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            var pendingPresent = authority.PendingRelease is not null || authority.PendingGrantRevision is not null || authority.PendingGrants.Length != 0;
            var pendingExact = authority.PendingRelease == reservation.Release &&
                authority.PendingGrantRevision is not null &&
                FeatureHubTransitions.SameGrants(authority.PendingGrants, reservationGrants);
            if (pendingPresent && !pendingExact)
                throw new FeatureConcurrencyException("The pending Feature authority does not match the exact reservation.");
            var expectedPendingRevision = checked(new[]
            {
                authority.ActiveGrantRevision?.Value ?? 0,
                authority.PreviousGrantRevision?.Value ?? 0
            }.Max() + 1);
            if (pendingExact && authority.PendingGrantRevision!.Value.Value != expectedPendingRevision)
                throw new FeatureConcurrencyException("The pending Feature grant revision is not the exact next authority revision.");

            if (authority.ActiveRelease is null)
            {
                if (!pendingExact || registrations.Length != 0 || authority.PreviousRelease is not null ||
                    authority.ActiveGrantRevision is not null || authority.ActiveGrants.Length != 0 ||
                    authority.PreviousGrantRevision is not null || authority.PreviousGrants.Length != 0 ||
                    authority.PreviousSubscriptions is not null || authority.Paused || authority.PauseReason is not null ||
                    authority.PublicationReceipt is not null || authority.RollbackReplay is not null)
                    throw new FeatureConcurrencyException("The uncommitted Feature authority contains non-reservation state.");
                authorities.RemoveAt(authorityIndex);
                requiresDiscard = true;
            }
            else
            {
                if (authority.ActiveGrantRevision is null || registrations.Length != 1 ||
                    registrations[0].Release != authority.ActiveRelease)
                    throw new FeatureConcurrencyException("The active Feature authority and registration are not exact.");
                if (authority.PreviousRelease is null != (authority.PreviousGrantRevision is null) ||
                    authority.PreviousRelease is null != (authority.PreviousSubscriptions is null) ||
                    authority.PreviousRelease is null && authority.PreviousGrants.Length != 0)
                    throw new FeatureConcurrencyException("The active Feature rollback authority is incomplete.");
                var candidateAlreadyActive = authority.ActiveRelease == reservation.Release &&
                    FeatureHubTransitions.SameGrants(authority.ActiveGrants, reservationGrants) &&
                    registrations[0].Subscriptions.SequenceEqual(reservationSubscriptions, StringComparer.Ordinal);
                var baseline = reservation.AuthorityBaseline is null
                    ? throw new FeatureConcurrencyException(
                        "The active Feature reservation has no authority baseline.",
                        FeatureCommandRejectionReason.Precondition)
                    : AuthorityFromBaseline(reservation.AuthorityBaseline, reservation.InstallationId, actorId);
                if (baseline.ActiveRelease != reservation.RuntimeActiveRelease ||
                    baseline.PreviousRelease != reservation.RuntimePreviousRelease)
                    throw new FeatureConcurrencyException("The reserved runtime and authority baselines disagree.");
                var sameReleaseCandidateActivated = candidateAlreadyActive &&
                    baseline.ActiveRelease == reservation.Release;
                var switchedCandidateActivated = candidateAlreadyActive &&
                    baseline.ActiveRelease != reservation.Release;
                if ((sameReleaseCandidateActivated || switchedCandidateActivated) &&
                    authority.PublicationReceipt is not null)
                    throw new FeatureConcurrencyException(
                        "A confirmed Feature candidate must complete through the exact installation command.",
                        FeatureCommandRejectionReason.Precondition);
                var expectedCandidateGrantRevision = new GrantRevision(checked(Math.Max(
                    baseline.ActiveGrantRevision!.Value.Value,
                    baseline.PreviousGrantRevision?.Value ?? 0L) + 1));
                if (switchedCandidateActivated)
                {
                    if (authority.ActiveGrantRevision != expectedCandidateGrantRevision ||
                        authority.PreviousRelease != baseline.ActiveRelease ||
                        authority.PreviousGrantRevision != baseline.ActiveGrantRevision ||
                        !FeatureHubTransitions.SameGrants(authority.PreviousGrants, baseline.ActiveGrants) ||
                        !SameSubscriptions(
                            authority.PreviousSubscriptions,
                            reservation.AuthorityBaseline.Registration.Subscriptions) ||
                        authority.Paused != baseline.Paused ||
                        !string.Equals(authority.PauseReason, baseline.PauseReason, StringComparison.Ordinal) ||
                        authority.PendingRelease is not null || authority.PendingGrantRevision is not null ||
                        authority.PendingGrants.Length != 0 || authority.RollbackReplay is not null)
                        throw new FeatureConcurrencyException("The activated Feature candidate is not an exact switch.");
                }
                else
                {
                    if (authority.ActiveRelease != baseline.ActiveRelease || authority.PreviousRelease != baseline.PreviousRelease ||
                        authority.PreviousGrantRevision != baseline.PreviousGrantRevision ||
                         !FeatureHubTransitions.SameGrants(authority.PreviousGrants, baseline.PreviousGrants) ||
                         authority.Paused != baseline.Paused ||
                         !string.Equals(authority.PauseReason, baseline.PauseReason, StringComparison.Ordinal) ||
                         !SameSubscriptions(authority.PreviousSubscriptions, baseline.PreviousSubscriptions) ||
                         !sameReleaseCandidateActivated && authority.RollbackReplay != baseline.RollbackReplay ||
                         sameReleaseCandidateActivated && authority.RollbackReplay is not null)
                        throw new FeatureConcurrencyException("The active Feature authority changed after reservation.");
                    if (!sameReleaseCandidateActivated &&
                        (authority.ActiveGrantRevision != baseline.ActiveGrantRevision ||
                         !FeatureHubTransitions.SameGrants(authority.ActiveGrants, baseline.ActiveGrants) ||
                         !SameRegistration(registrations[0], reservation.AuthorityBaseline.Registration)))
                        throw new FeatureConcurrencyException("The active Feature authority baseline is no longer exact.");
                    if (sameReleaseCandidateActivated &&
                        (authority.ActiveGrantRevision != expectedCandidateGrantRevision ||
                         authority.PendingRelease is not null || authority.PendingGrantRevision is not null ||
                         authority.PendingGrants.Length != 0))
                        throw new FeatureConcurrencyException("The activated same-release candidate retains pending authority.");
                    if (!sameReleaseCandidateActivated && !pendingExact && pendingPresent)
                        throw new FeatureConcurrencyException("The pending Feature authority does not match the reservation.");
                }
                var baselineRegistrationIndex = Array.FindIndex(
                    restoredInstallations,
                    candidate => candidate.InstallationId == reservation.InstallationId);
                if (baselineRegistrationIndex < 0)
                    throw new FeatureConcurrencyException("The active Feature registration is unavailable.");
                restoredInstallations[baselineRegistrationIndex] = reservation.AuthorityBaseline.Registration with
                {
                    Subscriptions = reservation.AuthorityBaseline.Registration.Subscriptions.ToArray()
                };
                var lifecycleChanged = pendingExact || sameReleaseCandidateActivated || switchedCandidateActivated;
                requiresRepublish = lifecycleChanged && !baseline.Paused;
                if (lifecycleChanged)
                {
                    var resetFence = Math.Max(authority.PublicationFence, baseline.PublicationFence);
                    if (requiresRepublish)
                        resetFence = checked(resetFence + 1);
                    preservedAuthority = baseline with
                    {
                        PublicationFence = resetFence,
                        PublicationReceipt = null
                    };
                }
                else
                {
                    preservedAuthority = baseline;
                }
                authorities[authorityIndex] = preservedAuthority;
            }
        }

        var cleanedState = state with
        {
            Approvals = approvals,
            Authorities = authorities.ToArray(),
            Installations = restoredInstallations
        };
        if (requiresRepublish)
        {
            var resets = state.DraftInstallationResets ?? [];
            if (resets.Length >= FeatureLimits.DraftInstallationReservations)
                throw new FeatureLimitExceededException("An Owner can have at most 100 active Feature installation resets.");
            var resetPublication = FeaturePublicationTransitions.Prepare(
                cleanedState,
                reservation.InstallationId);
            if (!ReferenceEquals(resetPublication.State, cleanedState) || resetPublication.Receipt is not null)
                throw new FeatureConcurrencyException(
                    "The reset publication fence was not prepared exactly.",
                    FeatureCommandRejectionReason.Precondition);
            var preparedState = cleanedState with
            {
                DraftInstallationResets =
                [
                    .. resets,
                    new FeatureDraftInstallationResetState(
                        command.DraftId,
                        command.IdempotencyId,
                        actorId,
                        resetAt,
                        reservation.InstallationId,
                        reservation.Release,
                        reservation.CommandDigest,
                        true,
                        resetPublication.Ticket.PublicationFence,
                        resetPublication.Ticket.AuthorityDigest,
                        resetPublication.Ticket.AccessDigest)
                ],
                Revision = nextRevision
            };
            DemandResetPreparationLedgerBudget(state, preparedState, command.DraftId);
            return new FeatureDraftInstallationResetTransition(
                preparedState,
                draft,
                false,
                requiresDiscard,
                requiresRuntimeAbsence,
                true,
                preservedAuthority,
                false);
        }

        var resetDraft = new FeatureDraft(
            draft.DraftId,
            draft.OriginatingRequest,
            draft.Goal,
            draft.Status,
            draft.Behavior,
            draft.Source,
            null,
            reservation.AuthorityBaseline is null ? null : reservation.InstallationId,
            NextRevision(draft.Revision),
            draft.CreatedAt,
            resetAt);
        var drafts = (state.Drafts ?? []).ToArray();
        var draftIndex = Array.FindIndex(drafts, candidate => candidate.DraftId == draft.DraftId);
        if (draftIndex < 0)
            throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        drafts[draftIndex] = resetDraft;
        DemandOwnerDraftBudget(drafts);
        cleanedState = cleanedState with
        {
            Drafts = drafts,
            DraftInstallationReservations = (state.DraftInstallationReservations ?? [])
                .Where(candidate => candidate.DraftId != command.DraftId)
                .ToArray(),
            DraftInstallationResets = (state.DraftInstallationResets ?? [])
                .Where(candidate => candidate.DraftId != command.DraftId)
                .ToArray()
        };
        var resultState = AppendReplay(
            cleanedState,
            draft,
            resetDraft,
            command.IdempotencyId,
            "installation-reset",
            payloadDigest,
            actorId);
        resultState = FeatureHubEvidenceLedger.CompactReleases(resultState);
        return new FeatureDraftInstallationResetTransition(
            resultState,
            resetDraft,
            false,
            requiresDiscard,
            requiresRuntimeAbsence,
            false,
            preservedAuthority,
            true);
    }

    public static FeatureDraftInstallationResetTransition CompleteInstallationReservationReset(
        FeatureHubState state,
        FeatureDraftId draftId,
        string idempotencyId,
        ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandDraftId(draftId);
        DemandText(idempotencyId, 256, nameof(idempotencyId));
        DemandText(actorId.Value, 256, nameof(actorId));
        var priorReplay = (state.DraftReplays ?? []).FirstOrDefault(candidate =>
            candidate.DraftId == draftId && string.Equals(candidate.IdempotencyId, idempotencyId, StringComparison.Ordinal));
        if (priorReplay is not null)
        {
            if ((state.DraftInstallationReservations ?? []).Any(candidate => candidate.DraftId == draftId))
                throw new FeatureConcurrencyException("A new Feature installation reservation supersedes this reset replay.");
            if (!string.Equals(priorReplay.Kind, "installation-reset", StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The idempotency identifier is already bound to another authoring command.");
            if (priorReplay.ActorId != actorId)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            var current = ReadDraft(state, draftId)
                ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
            return new FeatureDraftInstallationResetTransition(
                state,
                priorReplay.Result(current),
                true,
                false,
                false,
                false,
                null,
                true);
        }

        var reset = (state.DraftInstallationResets ?? []).SingleOrDefault(candidate => candidate.DraftId == draftId)
            ?? throw new FeatureConcurrencyException(
                "The Feature Draft has no installation reset awaiting completion.",
                FeatureCommandRejectionReason.Precondition);
        if (!string.Equals(reset.IdempotencyId, idempotencyId, StringComparison.Ordinal))
            throw new FeatureConcurrencyException("The Feature Draft has another installation reset in progress.");
        if (reset.ActorId != actorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        var reservation = ReadInstallationReservation(state, draftId)
            ?? throw new FeatureConcurrencyException("The reset installation reservation is unavailable.");
        if (reservation.ActorId != actorId || reservation.InstallationId != reset.InstallationId ||
            reservation.Release != reset.Release ||
            !string.Equals(reservation.CommandDigest, reset.CommandDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException("The reset obligation no longer matches the exact reservation.");
        var draft = DemandEditableDraft(state, draftId, reservation.DraftRevision, allowReserved: true);
        if (!reset.RequiresRepublish)
            throw new FeatureConcurrencyException("The reset obligation does not require two-phase completion.");
        if (reservation.Grants is null || reservation.Subscriptions is null)
            throw new FeatureConcurrencyException("A legacy reservation cannot have a publication reset obligation.");
        var canonicalInstallation = CanonicalInstallation(new InstallFeatureVersion(
            reservation.DraftId,
            reservation.DraftRevision,
            reservation.InstallationId,
            reservation.Release,
            reservation.Grants,
            reservation.Subscriptions,
            reservation.DecisionId,
            reservation.IdempotencyId,
            reservation.RuntimeRevision,
            reservation.RuntimeActiveRelease,
            reservation.RuntimePreviousRelease));
        DemandExactReservation(reservation, canonicalInstallation, actorId);
        var currentAuthority = DemandExactPreparedReset(
            state,
            reset,
            reservation,
            actorId,
            requireConfirmed: true);
        var resetDraft = new FeatureDraft(
            draft.DraftId,
            draft.OriginatingRequest,
            draft.Goal,
            draft.Status,
            draft.Behavior,
            draft.Source,
            null,
            reservation.AuthorityBaseline is null ? null : reservation.InstallationId,
            NextRevision(draft.Revision),
            draft.CreatedAt,
            reset.ResetAt);
        var drafts = (state.Drafts ?? []).ToArray();
        var draftIndex = Array.FindIndex(drafts, candidate => candidate.DraftId == draftId);
        if (draftIndex < 0)
            throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        drafts[draftIndex] = resetDraft;
        DemandOwnerDraftBudget(drafts);
        var cleaned = state with
        {
            Drafts = drafts,
            DraftInstallationReservations = (state.DraftInstallationReservations ?? [])
                .Where(candidate => candidate.DraftId != draftId)
                .ToArray(),
            DraftInstallationResets = (state.DraftInstallationResets ?? [])
                .Where(candidate => candidate.DraftId != draftId)
                .ToArray()
        };
        var completed = AppendReplay(
            cleaned,
            draft,
            resetDraft,
            idempotencyId,
            "installation-reset",
            ResetFingerprint(idempotencyId, actorId, canonicalInstallation),
            actorId);
        completed = FeatureHubEvidenceLedger.CompactReleases(completed);
        return new FeatureDraftInstallationResetTransition(
            completed,
            resetDraft,
            false,
            false,
            false,
            false,
            currentAuthority,
            true);
    }

    public static FeatureDraftAuthoringTransition ReviseBehavior(FeatureHubState state, ReviseFeatureBehavior command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.RevisedAt);
        var behavior = ValidateBehavior(command.Behavior);
        var digest = Fingerprint(command with { RevisedAt = default });
        if (Replay(
                state,
                command.DraftId,
                command.IdempotencyId,
                "behavior",
                digest,
                at => Fingerprint(command with { RevisedAt = at })) is { } replay)
            return replay;
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                behavior,
                draft.Source,
                null,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.RevisedAt),
            command.IdempotencyId,
            "behavior",
            digest);
    }

    public static FeatureDraftAuthoringTransition ReviseSource(FeatureHubState state, ReviseFeatureSource command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.RevisedAt);
        var source = ValidateSource(command.Source);
        var digest = Fingerprint(command with { RevisedAt = default });
        if (Replay(
                state,
                command.DraftId,
                command.IdempotencyId,
                "source",
                digest,
                at => Fingerprint(command with { RevisedAt = at })) is { } replay)
            return replay;
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                draft.Behavior,
                source,
                null,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.RevisedAt),
            command.IdempotencyId,
            "source",
            digest);
    }

    public static FeatureDraftAuthoringTransition AcceptSuggestedChange(FeatureHubState state, AcceptSuggestedChange command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Patch);
        var patch = ValidatePatch(command.Patch);
        DemandMutation(command.IdempotencyId, command.AcceptedAt);
        if (patch.BaseRevision != command.ExpectedRevision)
            throw new FeatureConcurrencyException("The Suggested Change does not target the expected Draft Revision.");
        var digest = Fingerprint(command with { AcceptedAt = default });
        if (Replay(
                state,
                patch.DraftId,
                command.IdempotencyId,
                "suggested-change",
                digest,
                at => Fingerprint(command with { AcceptedAt = at })) is { } replay)
            return replay;
        var draft = DemandEditableDraft(state, patch.DraftId, command.ExpectedRevision);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                patch.ReplacementBehavior,
                patch.ReplacementSource,
                null,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.AcceptedAt),
            command.IdempotencyId,
            "suggested-change",
            digest);
    }

    public static FeatureDraftAuthoringTransition RejectSuggestedChange(FeatureHubState state, RejectSuggestedChange command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandText(command.PatchId, FeatureLimits.DraftPatchIdCharacters, nameof(command.PatchId));
        if (command.BaseRevision != command.ExpectedRevision)
            throw new FeatureConcurrencyException("The Suggested Change does not target the expected Draft Revision.");
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision, allowReserved: true);
        return new FeatureDraftAuthoringTransition(state, draft);
    }

    public static FeatureDraftAuthoringTransition RecordVerification(FeatureHubState state, RecordFeatureVerification command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Verification);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.Verification.VerifiedAt);
        var verification = ValidateVerification(command.Verification);
        var digest = Fingerprint(command);
        if (Replay(state, command.DraftId, command.IdempotencyId, "verification", digest) is { } replay)
        {
            DemandVerificationSource(verification, replay.Draft.Source);
            return replay;
        }
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision);
        DemandVerificationSource(verification, draft.Source);
        return Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                draft.Status,
                draft.Behavior,
                draft.Source,
                verification,
                draft.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                verification.VerifiedAt),
            command.IdempotencyId,
            "verification",
            digest);
    }

    public static FeatureDraftAuthoringTransition MarkInstalled(FeatureHubState state, MarkFeatureDraftInstalled command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        DemandMutation(command.IdempotencyId, command.InstalledAt);
        DemandText(command.InstallationId.Value, 256, nameof(command.InstallationId));
        DemandRelease(command.Release);
        if ((state.DraftInstallationResets ?? []).Any(candidate => candidate.DraftId == command.DraftId))
            throw new FeatureConcurrencyException(
                "The Feature Draft has an installation reset in progress.",
                FeatureCommandRejectionReason.Precondition);
        var digest = Fingerprint(command);
        if (Replay(state, command.DraftId, command.IdempotencyId, "installed", digest) is { } replay)
            return replay;
        var reservation = ReadInstallationReservation(state, command.DraftId)
            ?? throw new FeatureConcurrencyException(
                "The Feature Draft has no active installation reservation.",
                FeatureCommandRejectionReason.Precondition);
        if (reservation.DraftRevision != command.ExpectedRevision ||
            reservation.InstallationId != command.InstallationId ||
            reservation.Release != command.Release ||
            !string.Equals(reservation.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal))
            throw new FeatureConcurrencyException("The Feature Draft installation reservation does not match this completion.");
        var draft = DemandEditableDraft(state, command.DraftId, command.ExpectedRevision, allowReserved: true);
        var verification = draft.Verification ?? throw new FeatureConcurrencyException(
            "The Feature Draft has no Verification to install.",
            FeatureCommandRejectionReason.Precondition);
        if (verification.Release != command.Release)
            throw new FeatureConcurrencyException(
                "The installed release must match the exact verified release.",
                FeatureCommandRejectionReason.Precondition);
        if (verification.Failed != 0 || verification.Skipped != 0 || verification.Passed != verification.Total)
            throw new FeatureConcurrencyException(
                "Only a fully successful Verification can be installed.",
                FeatureCommandRejectionReason.Precondition);
        FeaturePublicationTransitions.DemandConfirmedReservation(state, reservation);
        var installed = Replace(
            state,
            draft,
            new FeatureDraft(
                draft.DraftId,
                draft.OriginatingRequest,
                draft.Goal,
                "installed",
                draft.Behavior,
                draft.Source,
                verification,
                command.InstallationId,
                NextRevision(draft.Revision),
                draft.CreatedAt,
                command.InstalledAt),
            command.IdempotencyId,
            "installed",
            digest);
        return installed with
        {
            State = installed.State with
            {
                DraftInstallationReservations = (installed.State.DraftInstallationReservations ?? [])
                    .Where(candidate => candidate.DraftId != command.DraftId)
                    .ToArray()
            }
        };
    }

    private static FeatureDraft DemandEditableDraft(
        FeatureHubState state,
        FeatureDraftId draftId,
        long expectedRevision,
        bool allowReserved = false)
    {
        var draft = ReadDraft(state, draftId) ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        if (!string.Equals(draft.Status, "draft", StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "An installed Feature Draft is immutable.",
                FeatureCommandRejectionReason.Precondition);
        if (draft.Revision != expectedRevision)
            throw new FeatureConcurrencyException("The Draft Revision changed.");
        if (!allowReserved && (state.DraftInstallationReservations ?? []).Any(candidate => candidate.DraftId == draftId))
            throw new FeatureConcurrencyException(
                "The Feature Draft is reserved for installation.",
                FeatureCommandRejectionReason.Precondition);
        return draft;
    }

    private static FeatureDraftAuthoringTransition Replace(
        FeatureHubState state,
        FeatureDraft current,
        FeatureDraft replacement,
        string idempotencyId,
        string kind,
        string payloadDigest)
    {
        var drafts = (state.Drafts ?? []).ToArray();
        var index = Array.FindIndex(drafts, candidate => candidate.DraftId == current.DraftId);
        if (index < 0)
            throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        drafts[index] = replacement;
        DemandOwnerDraftBudget(drafts);
        var next = AppendReplay(
            state with { Drafts = drafts },
            current,
            replacement,
            idempotencyId,
            kind,
            payloadDigest);
        return new FeatureDraftAuthoringTransition(next, replacement);
    }

    private static FeatureHubState AppendReplay(
        FeatureHubState state,
        FeatureDraft current,
        FeatureDraft replacement,
        string idempotencyId,
        string kind,
        string payloadDigest,
        ActorId actorId = default)
    {
        var replays = (state.DraftReplays ?? []).ToList();
        while (replays.Count(replay => replay.DraftId == current.DraftId) >= FeatureLimits.DraftReplayRecords)
        {
            var oldest = replays.FindIndex(replay => replay.DraftId == current.DraftId);
            replays.RemoveAt(oldest);
        }
        var replay = new FeatureDraftCommandReplay(
            current.DraftId,
            idempotencyId,
            kind,
            payloadDigest,
            replacement.Status,
            replacement.Behavior,
            replacement.Source,
            replacement.Verification,
            replacement.InstallationId,
            replacement.Revision,
            replacement.UpdatedAt,
            0,
            actorId);
        replay = replay with { Utf8Bytes = ReplayFootprint(replay) };
        if (replay.Utf8Bytes > FeatureLimits.DraftReplayUtf8Bytes)
            throw new FeatureLimitExceededException("A Feature Draft replay exceeds its UTF-8 byte budget.");
        replays.Add(replay);
        while (replays.Count > 1 && replays.Sum(candidate => (long)candidate.Utf8Bytes) > FeatureLimits.DraftReplayUtf8Bytes)
            replays.RemoveAt(0);
        return state with { DraftReplays = replays.ToArray(), Revision = NextRevision(state.Revision) };
    }

    private static long NextRevision(long revision)
    {
        if (revision == long.MaxValue)
            throw new FeatureConcurrencyException("The Feature Draft Revision cannot advance.");
        return revision + 1;
    }

    private static FeatureDraftAuthoringTransition? Replay(
        FeatureHubState state,
        FeatureDraftId draftId,
        string idempotencyId,
        string kind,
        string payloadDigest,
        Func<DateTimeOffset, string>? legacyFingerprint = null)
    {
        DemandDraftId(draftId);
        var replay = (state.DraftReplays ?? []).FirstOrDefault(candidate =>
            candidate.DraftId == draftId && string.Equals(candidate.IdempotencyId, idempotencyId, StringComparison.Ordinal));
        if (replay is null)
            return null;
        var legacyPayloadMatches = legacyFingerprint is not null && string.Equals(
            replay.PayloadDigest,
            legacyFingerprint(replay.ResultUpdatedAt),
            StringComparison.Ordinal);
        if (!string.Equals(replay.Kind, kind, StringComparison.Ordinal) ||
            !string.Equals(replay.PayloadDigest, payloadDigest, StringComparison.Ordinal) && !legacyPayloadMatches)
            throw new FeatureConcurrencyException("The idempotency identifier is already bound to a different authoring command.");
        var current = (state.Drafts ?? []).FirstOrDefault(candidate => candidate.DraftId == draftId)
            ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        return new FeatureDraftAuthoringTransition(state, replay.Result(current));
    }

    private static FeatureVerification ValidateVerification(FeatureVerification verification)
    {
        DemandRelease(verification.Release);
        if (verification.Total is <= 0 or > FeatureLimits.DraftVerificationScenarios ||
            verification.Passed < 0 || verification.Failed < 0 || verification.Skipped < 0 ||
            (long)verification.Passed + verification.Failed + verification.Skipped != verification.Total)
            throw new ArgumentException("Verification result counts are invalid.", nameof(verification));
        if (verification.VerifiedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Verification timestamps must be UTC.", nameof(verification));
        var evidence = verification.Evidence
            ?? throw new ArgumentException("Verification evidence is required.", nameof(verification));
        ValidateVerificationEvidence(verification, evidence);
        return verification;
    }

    private static void ValidateVerificationEvidence(
        FeatureVerification verification,
        FeatureVerificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence.Scenarios);
        ArgumentNullException.ThrowIfNull(evidence.Artifacts);
        if (!CanonicalSourceReference(evidence.SourceReference) ||
            evidence.Total != verification.Total || evidence.Passed != verification.Passed ||
            evidence.Failed != verification.Failed || evidence.Skipped != verification.Skipped ||
            evidence.Scenarios.Length != evidence.Total)
            throw new ArgumentException("Verification evidence coordinates are invalid.", nameof(verification));
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var scenario in evidence.Scenarios)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            DemandText(
                scenario.ScenarioId,
                FeatureLimits.DraftVerificationScenarioIdCharacters,
                nameof(verification));
            DemandText(
                scenario.Name,
                FeatureLimits.DraftVerificationScenarioNameCharacters,
                nameof(verification));
            if (!scenarioIds.Add(scenario.ScenarioId) ||
                scenario.DurationMilliseconds is < 0 or > FeatureLimits.DraftVerificationDurationMilliseconds)
                throw new ArgumentException("Verification scenario evidence is invalid.", nameof(verification));
            switch (scenario.Outcome)
            {
                case FeatureScenarioOutcome.Passed:
                    passed++;
                    if (scenario.SafeFailure is not null)
                        throw new ArgumentException("Passing scenario evidence cannot contain a failure.", nameof(verification));
                    break;
                case FeatureScenarioOutcome.Failed:
                    failed++;
                    DemandText(
                        scenario.SafeFailure!,
                        FeatureLimits.DraftVerificationSafeFailureCharacters,
                        nameof(verification));
                    break;
                case FeatureScenarioOutcome.Skipped:
                    skipped++;
                    if (scenario.SafeFailure is { } skippedReason)
                        DemandText(
                            skippedReason,
                            FeatureLimits.DraftVerificationSafeFailureCharacters,
                            nameof(verification));
                    break;
                default:
                    throw new ArgumentException("Verification scenario outcome is invalid.", nameof(verification));
            }
        }
        if (passed != evidence.Passed || failed != evidence.Failed || skipped != evidence.Skipped)
            throw new ArgumentException("Verification scenario totals are invalid.", nameof(verification));
        if (evidence.Artifacts.Length > FeatureLimits.DraftVerificationArtifacts)
            throw new ArgumentException("Verification artifacts exceed their count bound.", nameof(verification));
        var artifactNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in evidence.Artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            DemandText(
                artifact.Name,
                FeatureLimits.DraftVerificationArtifactNameCharacters,
                nameof(verification));
            DemandText(
                artifact.MediaType,
                FeatureLimits.DraftVerificationArtifactMediaTypeCharacters,
                nameof(verification));
            if (!artifactNames.Add(artifact.Name) ||
                artifact.SizeBytes is < 0 or > FeatureLimits.DraftVerificationArtifactBytes ||
                !CanonicalSourceReference(artifact.Digest))
                throw new ArgumentException("Verification artifact evidence is invalid.", nameof(verification));
        }
        if (VerificationEvidenceUtf8Bytes(evidence) > FeatureLimits.DraftVerificationEvidenceUtf8Bytes)
            throw new FeatureLimitExceededException("Verification evidence exceeds its UTF-8 byte budget.");
    }

    internal static FeatureDraftPatch ValidatePatch(FeatureDraftPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        DemandText(patch.PatchId, FeatureLimits.DraftPatchIdCharacters, nameof(patch.PatchId));
        DemandDraftId(patch.DraftId);
        if (patch.BaseRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(patch), "A nonnegative base Draft Revision is required.");
        DemandText(patch.Summary, FeatureLimits.DraftPatchSummaryCharacters, nameof(patch.Summary));
        return patch with
        {
            ReplacementBehavior = ValidateBehavior(patch.ReplacementBehavior),
            ReplacementSource = ValidateSource(patch.ReplacementSource)
        };
    }

    private static void DemandRelease(ReleaseDigest release)
    {
        if (string.IsNullOrEmpty(release.Value) || release.Value.Length != 64 || release.Value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical release digest is required.", nameof(release));
    }

    private static InstallFeatureVersion CanonicalInstallation(InstallFeatureVersion command)
    {
        ArgumentNullException.ThrowIfNull(command);
        DemandDraftId(command.DraftId);
        if (command.ExpectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(command));
        DemandText(command.InstallationId.Value, 256, nameof(command.InstallationId));
        DemandRelease(command.Release);
        DemandText(command.DecisionId, 256, nameof(command.DecisionId));
        DemandText(command.IdempotencyId, 256, nameof(command.IdempotencyId));
        if (command.RuntimeRevision is < 0)
            throw new ArgumentOutOfRangeException(nameof(command), "A nonnegative runtime Revision is required.");
        if (command.RuntimeRevision is null != (command.RuntimeActiveRelease is null))
            throw new ArgumentException("The runtime Revision and active release baseline must be supplied together.", nameof(command));
        if (command.RuntimePreviousRelease is not null && command.RuntimeActiveRelease is null)
            throw new ArgumentException("A previous runtime release requires an active runtime release baseline.", nameof(command));
        if (command.RuntimeActiveRelease is { } runtimeActive)
            DemandRelease(runtimeActive);
        if (command.RuntimePreviousRelease is { } runtimePrevious)
            DemandRelease(runtimePrevious);
        var grants = FeatureHubTransitions.ValidateGrants(command.Grants);
        DemandSubscriptions(command.Subscriptions);
        return command with
        {
            Grants = grants.Select(grant => new FeatureGrantSpec(
                grant.CapabilityId,
                grant.CapabilityVersion,
                grant.ProviderConnectionId,
                grant.ConstraintsJson,
                grant.Provider)).ToArray(),
            Subscriptions = command.Subscriptions.Order(StringComparer.Ordinal).ToArray()
        };
    }

    private static FeatureInstallationAuthorityState DemandExactPreparedReset(
        FeatureHubState state,
        FeatureDraftInstallationResetState reset,
        FeatureDraftInstallationReservation reservation,
        ActorId actorId,
        bool requireConfirmed)
    {
        if (reset.TargetPublicationFence < 1 ||
            !IsCanonicalDigest(reset.TargetAuthorityDigest) ||
            !IsCanonicalDigest(reset.TargetAccessDigest))
            throw new FeatureConcurrencyException(
                "The prepared Feature reset publication binding is invalid.",
                FeatureCommandRejectionReason.Precondition);
        var baselineRecord = reservation.AuthorityBaseline
            ?? throw new FeatureConcurrencyException(
                "The reset reservation has no authority baseline.",
                FeatureCommandRejectionReason.Precondition);
        var baseline = AuthorityFromBaseline(baselineRecord, reservation.InstallationId, actorId);
        var authorities = state.Authorities
            .Where(candidate => candidate.InstallationId == reservation.InstallationId)
            .ToArray();
        var registrations = state.Installations
            .Where(candidate => candidate.InstallationId == reservation.InstallationId)
            .ToArray();
        if (authorities.Length != 1 || registrations.Length != 1)
            throw new FeatureConcurrencyException(
                "The prepared Feature reset coordinate is unavailable.",
                FeatureCommandRejectionReason.Precondition);
        var current = authorities[0];
        if (current.InstallationId != baseline.InstallationId || current.ActorId != baseline.ActorId ||
            current.ActiveRelease != baseline.ActiveRelease || current.PreviousRelease != baseline.PreviousRelease ||
            current.ActiveGrantRevision != baseline.ActiveGrantRevision ||
            !FeatureHubTransitions.SameGrants(current.ActiveGrants, baseline.ActiveGrants) ||
            current.PreviousGrantRevision != baseline.PreviousGrantRevision ||
            !FeatureHubTransitions.SameGrants(current.PreviousGrants, baseline.PreviousGrants) ||
            current.PendingRelease is not null || current.PendingGrantRevision is not null ||
            current.PendingGrants.Length != 0 || current.Paused != baseline.Paused ||
            !string.Equals(current.PauseReason, baseline.PauseReason, StringComparison.Ordinal) ||
            !SameSubscriptions(current.PreviousSubscriptions, baseline.PreviousSubscriptions) ||
            current.RollbackReplay != baseline.RollbackReplay ||
            !SameRegistration(registrations[0], baselineRecord.Registration))
            throw new FeatureConcurrencyException(
                "The prepared Feature reset authority no longer matches its baseline.",
                FeatureCommandRejectionReason.Precondition);
        FeaturePublicationTransition prepared;
        try
        {
            prepared = FeaturePublicationTransitions.Prepare(state, reservation.InstallationId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new FeatureConcurrencyException(
                "The prepared Feature reset publication is invalid.",
                FeatureCommandRejectionReason.Precondition);
        }
        if (!ReferenceEquals(prepared.State, state) ||
            prepared.Ticket.PublicationFence != reset.TargetPublicationFence ||
            !string.Equals(prepared.Ticket.AuthorityDigest, reset.TargetAuthorityDigest, StringComparison.Ordinal) ||
            !string.Equals(prepared.Ticket.AccessDigest, reset.TargetAccessDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The prepared Feature reset publication binding changed.",
                FeatureCommandRejectionReason.Precondition);
        if (prepared.Receipt is { } receipt)
        {
            if (receipt.InstallationId != reservation.InstallationId ||
                receipt.PublicationFence != reset.TargetPublicationFence ||
                !string.Equals(receipt.AuthorityDigest, reset.TargetAuthorityDigest, StringComparison.Ordinal) ||
                !string.Equals(receipt.AccessDigest, reset.TargetAccessDigest, StringComparison.Ordinal) ||
                !IsCanonicalDigest(receipt.ManifestDigest))
                throw new FeatureConcurrencyException(
                    "The prepared Feature reset publication receipt conflicts with its binding.",
                    FeatureCommandRejectionReason.Precondition);
        }
        else if (requireConfirmed)
        {
            throw new FeatureConcurrencyException(
                "The prepared Feature reset publication is not durably confirmed.",
                FeatureCommandRejectionReason.Precondition);
        }
        return current;
    }

    private static bool IsCanonicalDigest(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static FeatureInstallationAuthorityBaseline AuthorityBaseline(
        FeatureInstallationAuthorityState authority,
        FeatureInstallationRegistration registration)
    {
        if (authority.ActiveRelease is not { } activeRelease || authority.ActiveGrantRevision is not { } activeRevision ||
            registration.InstallationId != authority.InstallationId || registration.Release != activeRelease)
            throw new FeatureConcurrencyException(
                "The active Feature authority is incomplete.",
                FeatureCommandRejectionReason.Precondition);
        return new FeatureInstallationAuthorityBaseline(
            authority.InstallationId,
            authority.ActorId,
            activeRelease,
            authority.PreviousRelease,
            activeRevision,
            authority.ActiveGrants.Select(GrantSpec).ToArray(),
            authority.PreviousGrantRevision,
            authority.PreviousGrants.Select(GrantSpec).ToArray(),
            authority.Paused,
            authority.PauseReason,
            authority.PublicationFence,
            authority.PublicationReceipt,
            authority.PreviousSubscriptions?.ToArray(),
            authority.RollbackReplay is null
                ? null
                : new FeatureInstallationRollbackReplayBaseline(
                    authority.RollbackReplay.InstallationId,
                    authority.RollbackReplay.ExpectedActiveRelease,
                    authority.RollbackReplay.TargetRelease,
                    authority.RollbackReplay.ExpectedRevision,
                    authority.RollbackReplay.IdempotencyId,
                    authority.RollbackReplay.ResultAccessDigest),
            registration with { Subscriptions = registration.Subscriptions.ToArray() });
    }

    private static FeatureInstallationAuthorityState AuthorityFromBaseline(
        FeatureInstallationAuthorityBaseline baseline,
        FeatureInstallationId installationId,
        ActorId actorId)
    {
        try
        {
            return AuthorityFromBaselineCore(baseline, installationId, actorId);
        }
        catch (FeatureConcurrencyException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        {
            throw new FeatureConcurrencyException(
                "The reserved Feature authority baseline is invalid.",
                FeatureCommandRejectionReason.Precondition);
        }
    }

    private static FeatureInstallationAuthorityState AuthorityFromBaselineCore(
        FeatureInstallationAuthorityBaseline baseline,
        FeatureInstallationId installationId,
        ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(baseline.Registration);
        DemandText(installationId.Value, 256, nameof(installationId));
        DemandText(actorId.Value, 256, nameof(actorId));
        DemandText(baseline.InstallationId.Value, 256, nameof(baseline.InstallationId));
        DemandText(baseline.ActorId.Value, 256, nameof(baseline.ActorId));
        DemandRelease(baseline.ActiveRelease);
        if (baseline.PreviousRelease is { } previousRelease) DemandRelease(previousRelease);
        DemandRelease(baseline.Registration.Release);
        if (baseline.InstallationId != installationId || baseline.ActorId != actorId ||
            baseline.Registration.InstallationId != installationId ||
            baseline.Registration.Release != baseline.ActiveRelease || baseline.PublicationFence < 0 ||
            baseline.ActiveGrantRevision.Value <= 0 ||
            baseline.PreviousRelease is null != (baseline.PreviousGrantRevision is null) ||
            baseline.PreviousRelease is null != (baseline.PreviousSubscriptions is null) ||
            baseline.PreviousRelease is null && (baseline.PreviousGrants?.Length ?? -1) != 0 ||
            baseline.PreviousGrantRevision is { } previousGrantRevision && previousGrantRevision.Value <= 0 ||
            baseline.Paused != (baseline.PauseReason is not null))
            throw new FeatureConcurrencyException(
                "The reserved Feature authority baseline is incomplete.",
                FeatureCommandRejectionReason.Precondition);
        if (baseline.PauseReason is not null)
            DemandText(baseline.PauseReason, 512, nameof(baseline.PauseReason));
        ArgumentNullException.ThrowIfNull(baseline.ActiveGrants);
        ArgumentNullException.ThrowIfNull(baseline.PreviousGrants);
        var activeGrants = FeatureHubTransitions.ValidateGrants(baseline.ActiveGrants);
        var previousGrants = FeatureHubTransitions.ValidateGrants(baseline.PreviousGrants);
        if (!baseline.ActiveGrants.SequenceEqual(activeGrants.Select(GrantSpec)) ||
            !baseline.PreviousGrants.SequenceEqual(previousGrants.Select(GrantSpec)))
            throw new FeatureConcurrencyException(
                "The reserved Feature authority grants are not canonical.",
                FeatureCommandRejectionReason.Precondition);
        DemandSubscriptions(baseline.Registration.Subscriptions);
        if (!baseline.Registration.Subscriptions.SequenceEqual(
                baseline.Registration.Subscriptions.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
            throw new FeatureConcurrencyException(
                "The reserved Feature registration is not canonical.",
                FeatureCommandRejectionReason.Precondition);
        if (baseline.PreviousSubscriptions is not null)
        {
            DemandSubscriptions(baseline.PreviousSubscriptions);
            if (!baseline.PreviousSubscriptions.SequenceEqual(
                    baseline.PreviousSubscriptions.Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
                throw new FeatureConcurrencyException(
                    "The reserved Feature rollback subscriptions are not canonical.",
                    FeatureCommandRejectionReason.Precondition);
        }
        FeatureRollbackReplay? rollback = null;
        if (baseline.RollbackReplay is { } rollbackBaseline)
        {
            DemandText(rollbackBaseline.InstallationId.Value, 256, nameof(rollbackBaseline.InstallationId));
            DemandRelease(rollbackBaseline.ExpectedActiveRelease);
            DemandRelease(rollbackBaseline.TargetRelease);
            DemandText(rollbackBaseline.IdempotencyId, 256, nameof(rollbackBaseline.IdempotencyId));
            if (rollbackBaseline.InstallationId != installationId ||
                rollbackBaseline.TargetRelease != baseline.ActiveRelease ||
                rollbackBaseline.ExpectedActiveRelease == rollbackBaseline.TargetRelease ||
                rollbackBaseline.ExpectedRevision < 0 ||
                !IsCanonicalDigest(rollbackBaseline.ResultAccessDigest))
                throw new FeatureConcurrencyException(
                    "The reserved Feature rollback replay is invalid.",
                    FeatureCommandRejectionReason.Precondition);
            var resultAccessDigest = FeaturePublicationTransitions.AccessDigest(
                installationId,
                baseline.ActiveRelease,
                activeGrants,
                baseline.Registration.Subscriptions);
            if (!string.Equals(resultAccessDigest, rollbackBaseline.ResultAccessDigest, StringComparison.Ordinal))
                throw new FeatureConcurrencyException(
                    "The reserved Feature rollback replay access binding is invalid.",
                    FeatureCommandRejectionReason.Precondition);
            rollback = new FeatureRollbackReplay(
                rollbackBaseline.InstallationId,
                rollbackBaseline.ExpectedActiveRelease,
                rollbackBaseline.TargetRelease,
                rollbackBaseline.ExpectedRevision,
                rollbackBaseline.IdempotencyId,
                rollbackBaseline.ResultAccessDigest);
        }
        var authority = new FeatureInstallationAuthorityState(
            installationId,
            actorId,
            baseline.ActiveRelease,
            baseline.PreviousRelease,
            baseline.ActiveGrantRevision,
            activeGrants,
            baseline.PreviousGrantRevision,
            previousGrants,
            null,
            null,
            [],
            baseline.Paused,
            baseline.PauseReason,
            baseline.PublicationFence,
            baseline.PublicationReceipt,
            baseline.PreviousSubscriptions?.ToArray(),
            rollback);
        if (baseline.PublicationReceipt is { } receipt)
        {
            if (baseline.PublicationFence < 1 || receipt.InstallationId != installationId ||
                receipt.PublicationFence != baseline.PublicationFence ||
                !IsCanonicalDigest(receipt.AuthorityDigest) || !IsCanonicalDigest(receipt.AccessDigest) ||
                !IsCanonicalDigest(receipt.ManifestDigest))
                throw new FeatureConcurrencyException(
                    "The reserved Feature publication baseline is invalid.",
                    FeatureCommandRejectionReason.Precondition);
            var baselineState = FeatureHubState.Empty with
            {
                Authorities = [authority],
                Installations = [baseline.Registration with
                {
                    Subscriptions = baseline.Registration.Subscriptions.ToArray()
                }]
            };
            var publication = FeaturePublicationTransitions.Prepare(baselineState, installationId);
            if (!ReferenceEquals(publication.State, baselineState) ||
                publication.Ticket.PublicationFence != receipt.PublicationFence ||
                !string.Equals(publication.Ticket.AuthorityDigest, receipt.AuthorityDigest, StringComparison.Ordinal) ||
                !string.Equals(publication.Ticket.AccessDigest, receipt.AccessDigest, StringComparison.Ordinal))
                throw new FeatureConcurrencyException(
                    "The reserved Feature publication receipt does not match its authority.",
                    FeatureCommandRejectionReason.Precondition);
        }
        return authority;
    }

    private static FeatureGrantSpec GrantSpec(FeatureGrantState grant) => new(
        grant.CapabilityId,
        grant.CapabilityVersion,
        grant.ProviderConnectionId,
        grant.ConstraintsJson,
        grant.Provider);

    private static bool SameSubscriptions(string[]? left, string[]? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null && left.SequenceEqual(right, StringComparer.Ordinal);

    private static bool SameRegistration(
        FeatureInstallationRegistration left,
        FeatureInstallationRegistration right) =>
        left.InstallationId == right.InstallationId && left.Release == right.Release &&
        left.Subscriptions.SequenceEqual(right.Subscriptions, StringComparer.Ordinal);

    private static void DemandExactReservation(
        FeatureDraftInstallationReservation reservation,
        InstallFeatureVersion command,
        ActorId actorId)
    {
        var commandDigest = FeatureInstallationReservationDigests.Command(command);
        var accessDigest = FeatureInstallationReservationDigests.Access(
            command.InstallationId,
            command.Release,
            command.Grants,
            command.Subscriptions);
        if (reservation.DraftId != command.DraftId || reservation.DraftRevision != command.ExpectedRevision ||
            reservation.InstallationId != command.InstallationId || reservation.Release != command.Release ||
            reservation.ActorId != actorId ||
            !string.Equals(reservation.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal) ||
            !string.Equals(reservation.CommandDigest, commandDigest, StringComparison.Ordinal) ||
            !string.Equals(reservation.AccessDigest, accessDigest, StringComparison.Ordinal) ||
            !string.Equals(reservation.DecisionId, command.DecisionId, StringComparison.Ordinal) ||
            reservation.RuntimeRevision != command.RuntimeRevision ||
            reservation.RuntimeActiveRelease != command.RuntimeActiveRelease ||
            reservation.RuntimePreviousRelease != command.RuntimePreviousRelease ||
            reservation.Grants is null || !reservation.Grants.SequenceEqual(command.Grants) ||
            reservation.Subscriptions is null ||
            !reservation.Subscriptions.SequenceEqual(command.Subscriptions, StringComparer.Ordinal))
            throw new FeatureConcurrencyException("The reset command does not match the exact Feature installation reservation.");
    }

    private static void ValidateLegacyReservation(FeatureDraftInstallationReservation reservation)
    {
        DemandDraftId(reservation.DraftId);
        if (reservation.DraftRevision < 0)
            throw new FeatureConcurrencyException("The legacy Feature installation reservation has an invalid Draft Revision.");
        DemandText(reservation.InstallationId.Value, 256, nameof(reservation.InstallationId));
        DemandRelease(reservation.Release);
        DemandText(reservation.IdempotencyId, 256, nameof(reservation.IdempotencyId));
        DemandText(reservation.DecisionId, 256, nameof(reservation.DecisionId));
        DemandDigest(reservation.CommandDigest, nameof(reservation.CommandDigest));
        DemandDigest(reservation.AccessDigest, nameof(reservation.AccessDigest));
    }

    private static void DemandDigest(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64 ||
            value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical digest is required.", parameterName);
    }

    internal static void DemandInstallationLedgerBudget(
        FeatureDraftInstallationReservation[] reservations,
        FeatureDraftInstallationResetState[] resets)
    {
        var bytes = InstallationLedgerUtf8Bytes(reservations, resets);
        if (bytes > FeatureLimits.DraftInstallationLedgerUtf8Bytes)
            throw new FeatureLimitExceededException(
                "The Feature installation recovery ledger exceeds its bounded UTF-8 budget.");
    }

    private static void DemandResetPreparationLedgerBudget(
        FeatureHubState current,
        FeatureHubState prepared,
        FeatureDraftId draftId)
    {
        var preparedReservations = prepared.DraftInstallationReservations ?? [];
        var preparedResets = prepared.DraftInstallationResets ?? [];
        if (InstallationLedgerUtf8Bytes(preparedReservations, preparedResets) <=
            FeatureLimits.DraftInstallationLedgerUtf8Bytes)
            return;
        var currentReservations = current.DraftInstallationReservations ?? [];
        var currentResets = current.DraftInstallationResets ?? [];
        var unrelatedReservations = currentReservations
            .Where(candidate => candidate.DraftId != draftId)
            .ToArray();
        var unrelatedResets = currentResets
            .Where(candidate => candidate.DraftId != draftId)
            .ToArray();
        var currentTarget = currentReservations.Where(candidate => candidate.DraftId == draftId).ToArray();
        var preparedTarget = preparedReservations.Where(candidate => candidate.DraftId == draftId).ToArray();
        var preparedReset = preparedResets.Where(candidate => candidate.DraftId == draftId).ToArray();
        var unrelatedPreparedReservations = preparedReservations
            .Where(candidate => candidate.DraftId != draftId)
            .ToArray();
        var unrelatedPreparedResets = preparedResets
            .Where(candidate => candidate.DraftId != draftId)
            .ToArray();
        var exactRecoveryProgress =
            InstallationLedgerUtf8Bytes(unrelatedReservations, unrelatedResets) >
                FeatureLimits.DraftInstallationLedgerUtf8Bytes &&
            currentTarget.Length == 1 && preparedTarget.Length == 1 &&
            currentTarget[0] == preparedTarget[0] &&
            currentResets.All(candidate => candidate.DraftId != draftId) &&
            preparedReset.Length == 1 &&
            preparedReset[0].InstallationId == currentTarget[0].InstallationId &&
            preparedReset[0].Release == currentTarget[0].Release &&
            preparedReset[0].ActorId == currentTarget[0].ActorId &&
            string.Equals(preparedReset[0].CommandDigest, currentTarget[0].CommandDigest, StringComparison.Ordinal) &&
            unrelatedReservations.SequenceEqual(unrelatedPreparedReservations) &&
            unrelatedResets.SequenceEqual(unrelatedPreparedResets);
        if (!exactRecoveryProgress)
            throw new FeatureLimitExceededException(
                "The Feature installation recovery ledger exceeds its bounded UTF-8 budget.");
    }

    internal static int InstallationLedgerUtf8Bytes(
        FeatureDraftInstallationReservation[] reservations,
        FeatureDraftInstallationResetState[] resets) =>
        JsonSerializer.SerializeToUtf8Bytes(new InstallationLedgerPayload(reservations, resets)).Length;

    private static string ResetFingerprint(string resetId, ActorId actorId, InstallFeatureVersion installation) =>
        Fingerprint(new ResetReplayPayload(resetId, actorId, installation));

    private static string ResetFingerprint(
        string resetId,
        ActorId actorId,
        FeatureDraftInstallationReservation reservation) =>
        Fingerprint(new LegacyResetReplayPayload(
            resetId,
            actorId,
            reservation.DraftId,
            reservation.DraftRevision,
            reservation.InstallationId,
            reservation.Release,
            reservation.IdempotencyId,
            reservation.CommandDigest,
            reservation.AccessDigest,
            reservation.DecisionId));

    private static bool CanonicalSourceReference(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        !value[7..].Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'));

    private static void DemandVerificationSource(
        FeatureVerification verification,
        FeatureSourceSnapshot source)
    {
        var expected = SourceReference(source);
        if (!string.Equals(verification.Evidence!.SourceReference, expected, StringComparison.Ordinal))
            throw new ArgumentException("Verification evidence must bind the current Source Snapshot.", nameof(verification));
    }

    internal static string SourceReference(FeatureSourceSnapshot source) =>
        FeatureSourceReference.Compute(
            source.ImplementationProjectPath,
            source.ScenarioProjectPath,
            source.Files.Select(static file => (file.Path, file.Content)));

    private static string Fingerprint<T>(T command) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));

    private sealed record InstallationLedgerPayload(
        FeatureDraftInstallationReservation[] Reservations,
        FeatureDraftInstallationResetState[] Resets);

    private sealed record ResetReplayPayload(
        string ResetId,
        ActorId ActorId,
        InstallFeatureVersion Installation);

    private sealed record LegacyResetReplayPayload(
        string ResetId,
        ActorId ActorId,
        FeatureDraftId DraftId,
        long DraftRevision,
        FeatureInstallationId InstallationId,
        ReleaseDigest Release,
        string InstallationIdempotencyId,
        string CommandDigest,
        string AccessDigest,
        string DecisionId);

    private static FeatureBehavior ValidateBehavior(FeatureBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(behavior.Scenarios);
        if (behavior.Scenarios.Length is 0 or > FeatureLimits.DraftScenarios)
            throw new ArgumentException("Behavior must contain a bounded set of Scenarios.", nameof(behavior));
        var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
        var utf8Bytes = 0;
        foreach (var scenario in behavior.Scenarios)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            DemandText(scenario.ScenarioId, FeatureLimits.DraftScenarioIdCharacters, nameof(scenario.ScenarioId));
            DemandText(scenario.Name, FeatureLimits.DraftScenarioNameCharacters, nameof(scenario.Name));
            DemandText(scenario.Given, FeatureLimits.DraftScenarioStepCharacters, nameof(scenario.Given));
            DemandText(scenario.When, FeatureLimits.DraftScenarioStepCharacters, nameof(scenario.When));
            DemandText(scenario.Then, FeatureLimits.DraftScenarioStepCharacters, nameof(scenario.Then));
            if (!scenarioIds.Add(scenario.ScenarioId))
                throw new ArgumentException("Scenario identifiers must be unique.", nameof(behavior));
            utf8Bytes = checked(utf8Bytes + Encoding.UTF8.GetByteCount(scenario.ScenarioId) + Encoding.UTF8.GetByteCount(scenario.Name) +
                Encoding.UTF8.GetByteCount(scenario.Given) + Encoding.UTF8.GetByteCount(scenario.When) + Encoding.UTF8.GetByteCount(scenario.Then));
            if (utf8Bytes > FeatureLimits.DraftBehaviorUtf8Bytes)
                throw new ArgumentException("Behavior exceeds its UTF-8 bound.", nameof(behavior));
        }
        return behavior;
    }

    internal static FeatureSourceSnapshot ValidateSource(FeatureSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var implementationProject = ValidatePath(source.ImplementationProjectPath, nameof(source.ImplementationProjectPath));
        var scenarioProject = ValidatePath(source.ScenarioProjectPath, nameof(source.ScenarioProjectPath));
        if (!implementationProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            !scenarioProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source Snapshot entry paths must be C# projects.", nameof(source));
        ArgumentNullException.ThrowIfNull(source.Files);
        if (source.Files.Length is 0 or > FeatureLimits.DraftSourceFiles)
            throw new ArgumentException("Source Snapshot file count is outside its bound.", nameof(source));
        var collisionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exactPaths = new HashSet<string>(StringComparer.Ordinal);
        var utf8Bytes = 0;
        foreach (var file in source.Files)
        {
            ArgumentNullException.ThrowIfNull(file);
            var path = ValidatePath(file.Path, nameof(file.Path));
            if (!collisionPaths.Add(path))
                throw new ArgumentException("Source Snapshot paths must be unique.", nameof(source));
            exactPaths.Add(path);
            ArgumentNullException.ThrowIfNull(file.Content);
            if (file.Content.Contains('\0', StringComparison.Ordinal))
                throw new ArgumentException("Source files cannot contain null characters.", nameof(source));
            var fileBytes = Encoding.UTF8.GetByteCount(file.Content);
            if (fileBytes > FeatureLimits.DraftSourceFileUtf8Bytes)
                throw new ArgumentException("A Source file exceeds its UTF-8 bound.", nameof(source));
            utf8Bytes = checked(utf8Bytes + fileBytes);
            if (utf8Bytes > FeatureLimits.DraftSourceUtf8Bytes)
                throw new ArgumentException("Source Snapshot exceeds its UTF-8 bound.", nameof(source));
        }
        if (!exactPaths.Contains(implementationProject) || !exactPaths.Contains(scenarioProject))
            throw new ArgumentException("Both declared project paths must exist in the Source Snapshot.", nameof(source));
        return source;
    }

    private static string ValidatePath(string path, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(path, parameterName);
        var segments = path.Split('/');
        if (path.Length is 0 or > FeatureLimits.DraftSourcePathCharacters ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.StartsWith('/', StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ||
            segments.Any(segment => !IsPortablePathSegment(segment)))
            throw new ArgumentException("A bounded canonical relative Source path is required.", parameterName);
        return path;
    }

    private static bool IsPortablePathSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." ||
            !string.Equals(segment, segment.Trim(), StringComparison.Ordinal) ||
            segment.Any(char.IsControl) ||
            segment.IndexOfAny(InvalidSourcePathCharacters) >= 0 ||
            segment.EndsWith('.'))
            return false;
        var stem = segment.Split('.', 2)[0];
        return !ReservedSourcePathSegments.Contains(stem);
    }

    private static int ReplayFootprint(FeatureDraftCommandReplay replay)
    {
        long bytes = 256;
        bytes += Utf8(replay.DraftId.Value) + Utf8(replay.IdempotencyId) + Utf8(replay.Kind) + Utf8(replay.PayloadDigest) + Utf8(replay.ResultStatus);
        foreach (var scenario in replay.ResultBehavior.Scenarios)
            bytes += Utf8(scenario.ScenarioId) + Utf8(scenario.Name) + Utf8(scenario.Given) + Utf8(scenario.When) + Utf8(scenario.Then);
        bytes += Utf8(replay.ResultSource.ImplementationProjectPath) + Utf8(replay.ResultSource.ScenarioProjectPath);
        foreach (var file in replay.ResultSource.Files)
            bytes += Utf8(file.Path) + Encoding.UTF8.GetByteCount(file.Content);
        bytes += VerificationFootprint(replay.ResultVerification) + Utf8(replay.ResultInstallationId?.Value);
        return checked((int)bytes);
    }

    internal static long OwnerDraftUtf8Bytes(IReadOnlyList<FeatureDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        long bytes = 0;
        foreach (var draft in drafts)
        {
            ArgumentNullException.ThrowIfNull(draft);
            bytes = checked(bytes + DraftFootprint(draft));
        }
        return bytes;
    }

    internal static void DemandOwnerDraftBudget(IReadOnlyList<FeatureDraft> drafts)
    {
        if (OwnerDraftUtf8Bytes(drafts) > FeatureLimits.DraftOwnerUtf8Bytes)
            throw new FeatureLimitExceededException("Feature Drafts exceed the owner-wide live-state byte budget.");
    }

    private static long DraftFootprint(FeatureDraft draft)
    {
        long bytes = 512;
        var request = draft.OriginatingRequest;
        bytes += Utf8(draft.DraftId.Value) + Utf8(request.OperationId) + Utf8(request.ConversationId) + Utf8(request.Text);
        bytes += Utf8(draft.Goal) + Utf8(draft.Status);
        foreach (var scenario in draft.Behavior.Scenarios ?? [])
            bytes += Utf8(scenario.ScenarioId) + Utf8(scenario.Name) + Utf8(scenario.Given) + Utf8(scenario.When) + Utf8(scenario.Then);
        var source = draft.Source;
        bytes += Utf8(source.ImplementationProjectPath) + Utf8(source.ScenarioProjectPath);
        foreach (var file in source.Files ?? [])
            bytes += Utf8(file.Path) + Utf8(file.Content);
        bytes += VerificationFootprint(draft.Verification) + Utf8(draft.InstallationId?.Value);
        return bytes;
    }

    private static long VerificationFootprint(FeatureVerification? verification)
    {
        if (verification is null)
            return 0;
        long bytes = 96 + Utf8(verification.Release.Value);
        if (verification.Evidence is not { } evidence)
            return bytes;
        bytes += 64 + VerificationEvidenceUtf8Bytes(evidence);
        bytes += 48L * (evidence.Scenarios?.Length ?? 0);
        bytes += 48L * (evidence.Artifacts?.Length ?? 0);
        return bytes;
    }

    private static long VerificationEvidenceUtf8Bytes(FeatureVerificationEvidence evidence)
    {
        long bytes = Utf8(evidence.SourceReference);
        foreach (var scenario in evidence.Scenarios ?? [])
            bytes = checked(bytes + Utf8(scenario?.ScenarioId) + Utf8(scenario?.Name) + Utf8(scenario?.SafeFailure));
        foreach (var artifact in evidence.Artifacts ?? [])
            bytes = checked(bytes + Utf8(artifact?.Name) + Utf8(artifact?.MediaType) + Utf8(artifact?.Digest));
        return bytes;
    }

    private static int Utf8(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static void DemandDraftId(FeatureDraftId draftId)
    {
        ArgumentNullException.ThrowIfNull(draftId);
        DemandText(draftId.Value, 128, nameof(draftId));
    }

    private static void DemandSubscriptions(string[] subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        if (subscriptions.Length is 0 or > 64 || subscriptions.Any(subscription =>
                string.IsNullOrWhiteSpace(subscription) || subscription.Length > 256 ||
                subscription.Any(char.IsControl) ||
                !string.Equals(subscription, subscription.Trim(), StringComparison.Ordinal)) ||
            subscriptions.Distinct(StringComparer.Ordinal).Count() != subscriptions.Length)
            throw new ArgumentException("Canonical unique Feature subscriptions are required.", nameof(subscriptions));
    }

    private static void DemandMutation(string idempotencyId, DateTimeOffset at)
    {
        DemandText(idempotencyId, 256, nameof(idempotencyId));
        if (at.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature Draft mutation timestamps must be UTC.", nameof(at));
    }

    private static void DemandText(string value, int maximumCharacters, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumCharacters || value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical value is required.", parameterName);
    }
}
