using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Features;

internal static class FeatureHubTransitions
{
    public static FeatureCapabilityProjection[] ProjectCapabilities(
        FeatureHubState state,
        BrainOwnerId ownerId,
        ActorId actorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(ownerId.Value) || string.IsNullOrWhiteSpace(actorId.Value))
            return [];
        var projections = new List<FeatureCapabilityProjection>();
        foreach (var authority in state.Authorities
                     .Where(candidate => candidate.ActorId == actorId)
                     .OrderBy(candidate => candidate.InstallationId.Value, StringComparer.Ordinal))
        {
            if (authority.ActiveRelease is not { } release || authority.ActiveGrantRevision is not { } grantRevision ||
                authority.Paused || authority.PendingRelease is not null || authority.PendingGrantRevision is not null ||
                authority.PendingGrants.Length != 0)
                continue;
            FeaturePublicationTransition prepared;
            FeatureDraft? draft;
            try
            {
                prepared = FeaturePublicationTransitions.Prepare(state, authority.InstallationId);
                draft = FeatureDraftAuthoringTransitions.ReadInstalledDraft(state, authority.InstallationId, release);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or FeatureConcurrencyException or ArgumentException)
            {
                continue;
            }
            if (!ReferenceEquals(prepared.State, state) || prepared.Receipt is not { } receipt || draft is null ||
                receipt.PublicationFence != prepared.Ticket.PublicationFence ||
                !string.Equals(receipt.AuthorityDigest, prepared.Ticket.AuthorityDigest, StringComparison.Ordinal) ||
                !string.Equals(receipt.AccessDigest, prepared.Ticket.AccessDigest, StringComparison.Ordinal) ||
                prepared.Ticket.ActorId != actorId || prepared.Ticket.Release != release ||
                prepared.Ticket.GrantRevision != grantRevision)
                continue;
            var releaseMetadata = state.Releases.SingleOrDefault(candidate => candidate.Digest == release);
            if (releaseMetadata is null || !releaseMetadata.RequestedCapabilities
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(
                        prepared.Ticket.ActiveGrants.Select(grant => grant.CapabilityId).Order(StringComparer.Ordinal),
                        StringComparer.Ordinal))
                continue;
            var inputKind = prepared.Ticket.Subscriptions.Contains("manual", StringComparer.Ordinal)
                ? "manual"
                : prepared.Ticket.Subscriptions.Order(StringComparer.Ordinal).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(inputKind) || inputKind.Length > 128)
                continue;
            projections.Add(new FeatureCapabilityProjection(
                ownerId,
                authority.InstallationId,
                actorId,
                release,
                grantRevision,
                draft.Goal,
                draft.Behavior.Scenarios.ToArray(),
                prepared.Ticket.ActiveGrants.ToArray(),
                inputKind,
                prepared.Ticket.PublicationFence,
                prepared.Ticket.AuthorityDigest,
                prepared.Ticket.AccessDigest));
        }
        return projections.ToArray();
    }

    public static FeatureCapabilityProjection DemandFeatureRun(
        FeatureHubState state,
        BrainOwnerId ownerId,
        StartFeatureRun command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Input);
        if (ownerId != command.OwnerId)
            throw new FeatureConcurrencyException(
                "The Feature owner scope does not match the command.",
                FeatureCommandRejectionReason.Precondition);
        var authorities = state.Authorities
            .Where(candidate => candidate.InstallationId == command.InstallationId)
            .Take(2)
            .ToArray();
        if (authorities.Length != 1)
            throw new FeatureConcurrencyException(
                "The Feature installation authority is unavailable.",
                FeatureCommandRejectionReason.Precondition);
        if (authorities[0].ActorId != command.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (HasInstallationReservationOrReset(state, command.InstallationId))
            throw new FeatureConcurrencyException(
                "The Feature installation has an authoring reservation or reset in progress.",
                FeatureCommandRejectionReason.Precondition);
        var projections = ProjectCapabilities(state, ownerId, command.ActorId)
            .Where(candidate => candidate.InstallationId == command.InstallationId)
            .Take(2)
            .ToArray();
        if (projections.Length != 1)
            throw new FeatureConcurrencyException(
                "The Feature capability is not currently executable.",
                FeatureCommandRejectionReason.Precondition);
        var projection = projections[0];
        if (projection.Release != command.Release || projection.GrantRevision != command.GrantRevision ||
            projection.PublicationFence != command.PublicationFence ||
            !string.Equals(projection.AuthorityDigest, command.AuthorityDigest, StringComparison.Ordinal) ||
            !string.Equals(projection.AccessDigest, command.AccessDigest, StringComparison.Ordinal) ||
            !string.Equals(projection.InputKind, command.Input.Kind, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The Feature capability binding is stale or conflicts with the active authority.",
                FeatureCommandRejectionReason.Precondition);
        return projection;
    }

    public static FeatureHubState Propose(FeatureHubState state, FeatureReleaseProposal proposal, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(proposal);
        DemandRevision(state, expectedRevision);
        DemandNoInstallationReset(state, proposal.InstallationId);
        var release = ValidateRelease(proposal.Release);
        var grants = ValidateGrants(proposal.Grants);
        DemandExactReservationProposal(state, proposal.InstallationId, release.Digest, grants);
        if (!grants.Select(grant => grant.CapabilityId).Order(StringComparer.Ordinal).SequenceEqual(release.RequestedCapabilities.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ArgumentException("The proposal must bind one grant for every requested capability.", nameof(proposal));
        var boundRelease = state.Releases.SingleOrDefault(candidate => candidate.Digest == release.Digest);
        if (boundRelease is not null && !SameRelease(boundRelease, release))
            throw new FeatureConcurrencyException("The release digest is already bound to different metadata.");
        var currentApprovals = state.Approvals.Where(candidate =>
            candidate.InstallationId == proposal.InstallationId && candidate.Release.Digest == release.Digest &&
            candidate.Status != FeatureApprovalStatus.Superseded).ToArray();
        if (currentApprovals.Length > 1)
            throw new FeatureConcurrencyException("The release coordinate has ambiguous current approvals.");
        if (currentApprovals.Length == 1)
        {
            var existingApproval = currentApprovals[0];
            if (!SameRelease(existingApproval.Release, release) || !SameGrants(existingApproval.Grants, grants))
                throw new FeatureConcurrencyException("The release coordinate is already bound to a different current access plan.");
            return state;
        }
        FeatureHubEvidenceLedger.DemandOwnerCoordinateCapacity(state, proposal.InstallationId);
        FeatureHubEvidenceLedger.DemandCandidateAdmission(state, proposal.InstallationId, release.Digest);
        var active = state.Authorities.FirstOrDefault(candidate =>
            candidate.InstallationId == proposal.InstallationId)?.ActiveRelease;
        var priorCapabilities = active is { } digest
            ? state.Releases.FirstOrDefault(candidate => candidate.Digest == digest)?.RequestedCapabilities ?? []
            : [];
        var added = release.RequestedCapabilities.Except(priorCapabilities, StringComparer.Ordinal).ToArray();
        var removed = priorCapabilities.Except(release.RequestedCapabilities, StringComparer.Ordinal).ToArray();
        var nextRevision = checked(state.Revision + 1);
        var approval = new FeatureApprovalState(
            ApprovalId(proposal.InstallationId, release.Digest, nextRevision),
            proposal.InstallationId,
            release,
            added,
            removed,
            FeatureApprovalStatus.Pending,
            null,
            null,
            nextRevision,
            grants);
        var releases = state.Releases.Any(candidate => candidate.Digest == release.Digest)
            ? state.Releases
            : [.. state.Releases, release];
        var admittedApprovals = FeatureHubEvidenceLedger.AdmitProposal(
            state,
            proposal.InstallationId,
            nextRevision);
        return FeatureHubEvidenceLedger.CompactReleases(state with
        {
            Releases = releases,
            Approvals = FeatureApprovalLedger.Compact([.. admittedApprovals, approval]),
            Revision = nextRevision
        });
    }
    public static FeatureHubState Decide(FeatureHubState state, FeatureApprovalDecision decision, long expectedRevision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);
        DemandIdempotencyId(decision.DecisionId);
        if (decision.ActorId is not { } decisionActor)
            throw new FeatureConcurrencyException(
                "A Feature approval decision requires an actor binding.",
                FeatureCommandRejectionReason.Precondition);
        var index = Array.FindIndex(state.Approvals, candidate =>
            string.Equals(candidate.ApprovalId, decision.ApprovalId, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException("The feature approval does not exist.");
        var approval = state.Approvals[index];
        if (approval.Status != FeatureApprovalStatus.Pending)
        {
            if (approval.DecisionActorId != decisionActor)
                throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
            var exactStatus = decision.Approved
                ? FeatureApprovalStatus.Approved
                : FeatureApprovalStatus.Rejected;
            if (approval.Status == exactStatus && approval.Release.Digest == decision.Release &&
                string.Equals(approval.DecisionId, decision.DecisionId, StringComparison.Ordinal))
                return state;
            throw new FeatureConcurrencyException(
                "The feature approval already has a different decision.",
                FeatureCommandRejectionReason.Precondition);
        }
        DemandRevision(state, expectedRevision);
        DemandNoInstallationReset(state, approval.InstallationId);
        DemandExactReservationDecision(state, approval, decision);
        if (approval.Release.Digest != decision.Release)
            throw new FeatureConcurrencyException(
                "Approval is bound to another release digest.",
                FeatureCommandRejectionReason.Precondition);
        var nextRevision = checked(state.Revision + 1);
        var approvals = state.Approvals.ToArray();
        approvals[index] = approval with
        {
            Status = decision.Approved ? FeatureApprovalStatus.Approved : FeatureApprovalStatus.Rejected,
            DecisionId = decision.DecisionId,
            DecisionActorId = decisionActor,
            DecidedAt = now,
            Revision = nextRevision
        };
        return state with { Approvals = FeatureApprovalLedger.Compact(approvals), Revision = nextRevision };
    }
    public static FeatureHubState Grant(FeatureHubState state, FeatureGrantRequest request, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        DemandRevision(state, expectedRevision);
        DemandNoInstallationReset(state, request.InstallationId);
        var grants = ValidateGrants(request.Grants);
        DemandExactReservationGrant(state, request, grants);
        FeatureHubEvidenceLedger.DemandOwnerCoordinateCapacity(state, request.InstallationId);
        var index = Array.FindIndex(state.Authorities, candidate =>
            candidate.InstallationId == request.InstallationId);
        var current = index >= 0 ? state.Authorities[index] : null;
        if (current is not null && current.ActorId != request.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        var approvals = state.Approvals.Where(candidate =>
            candidate.InstallationId == request.InstallationId && candidate.Release.Digest == request.Release &&
            candidate.Status == FeatureApprovalStatus.Approved && candidate.DecisionActorId == request.ActorId &&
            SameGrants(candidate.Grants, grants)).ToArray();
        if (approvals.Length != 1)
            throw new FeatureConcurrencyException(
                "The exact release digest has not been approved.",
                FeatureCommandRejectionReason.Precondition);
        var approval = approvals[0];
        var greatestRevision = new[]
        {
            current?.ActiveGrantRevision?.Value ?? 0,
            current?.PreviousGrantRevision?.Value ?? 0,
            current?.PendingGrantRevision?.Value ?? 0
        }.Max();
        var authority = (current ?? new FeatureInstallationAuthorityState(request.InstallationId, request.ActorId, null, null, null, [], null, [], null, null, [], false, null)) with
        {
            ActorId = current?.ActorId ?? request.ActorId,
            PendingRelease = request.Release,
            PendingGrantRevision = new GrantRevision(checked(greatestRevision + 1)),
            PendingGrants = grants
        };
        authority = FeaturePublicationTransitions.Invalidate(authority);
        var authorities = state.Authorities.ToArray();
        if (index >= 0) authorities[index] = authority;
        else authorities = [.. authorities, authority];
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState Activate(FeatureHubState state, FeatureInstallationId installationId, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandRevision(state, expectedRevision);
        DemandNoInstallationReset(state, installationId);
        var index = AuthorityIndex(state, installationId);
        var authority = state.Authorities[index];
        DemandExactReservationActivation(state, authority);
        if (authority.PendingRelease is not { } pendingRelease || authority.PendingGrantRevision is not { } pendingRevision)
            throw new FeatureConcurrencyException(
                "The installation has no approved grant set staged.",
                FeatureCommandRejectionReason.Precondition);
        FeatureInstallationRegistration? previousRegistration = null;
        if (authority.ActiveRelease is { } activeRelease)
        {
            previousRegistration = state.Installations.SingleOrDefault(candidate => candidate.InstallationId == installationId);
            if (previousRegistration is null || previousRegistration.Release != activeRelease)
                throw new FeatureConcurrencyException(
                    "The active installation registration is not available for exact rollback.",
                    FeatureCommandRejectionReason.Precondition);
        }
        var authorities = state.Authorities.ToArray();
        var activated = pendingRelease == authority.ActiveRelease
            ? authority with
            {
                ActiveGrantRevision = pendingRevision,
                ActiveGrants = authority.PendingGrants,
                PendingRelease = null,
                PendingGrantRevision = null,
                PendingGrants = [],
                RollbackReplay = null
            }
            : authority with
            {
                PreviousRelease = authority.ActiveRelease,
                PreviousGrantRevision = authority.ActiveGrantRevision,
                PreviousGrants = authority.ActiveGrants,
                PreviousSubscriptions = previousRegistration?.Subscriptions.ToArray(),
                ActiveRelease = pendingRelease,
                ActiveGrantRevision = pendingRevision,
                ActiveGrants = authority.PendingGrants,
                PendingRelease = null,
                PendingGrantRevision = null,
                PendingGrants = [],
                RollbackReplay = null
            };
        authorities[index] = FeaturePublicationTransitions.Invalidate(activated);
        return FeatureHubEvidenceLedger.NormalizeLifecycleEvidence(
            state with { Authorities = authorities, Revision = checked(state.Revision + 1) },
            installationId);
    }
    public static FeatureHubState PauseAuthority(FeatureHubState state, FeatureInstallationId installationId, string reason, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandRevision(state, expectedRevision);
        DemandNoInstallationReservationOrReset(state, installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 512 || reason.Any(char.IsControl))
            throw new ArgumentException("A bounded safe pause reason is required.", nameof(reason));
        var index = AuthorityIndex(state, installationId);
        if (state.Authorities[index].Paused && string.Equals(state.Authorities[index].PauseReason, reason, StringComparison.Ordinal))
            return state;
        var authorities = state.Authorities.ToArray();
        authorities[index] = FeaturePublicationTransitions.Invalidate(
            authorities[index] with { Paused = true, PauseReason = reason });
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState ResumeAuthority(FeatureHubState state, FeatureInstallationId installationId, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        DemandRevision(state, expectedRevision);
        DemandNoInstallationReservationOrReset(state, installationId);
        var index = AuthorityIndex(state, installationId);
        if (!state.Authorities[index].Paused) return state;
        var authorities = state.Authorities.ToArray();
        authorities[index] = FeaturePublicationTransitions.Invalidate(
            authorities[index] with { Paused = false, PauseReason = null });
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState Revoke(FeatureHubState state, FeatureGrantRevocation revocation, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(revocation);
        DemandRevision(state, expectedRevision);
        DemandNoInstallationReservationOrReset(state, revocation.InstallationId);
        var index = AuthorityIndex(state, revocation.InstallationId);
        var authority = state.Authorities[index];
        var next = RemoveGrant(authority, revocation);
        if (ReferenceEquals(next, authority)) return state;
        var authorities = state.Authorities.ToArray();
        authorities[index] = FeaturePublicationTransitions.Invalidate(next);
        return state with { Authorities = authorities, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState RollbackAuthority(FeatureHubState state, RollbackFeatureInstallation command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        DemandIdempotencyId(command.IdempotencyId);
        DemandNoInstallationReservationOrReset(state, command.InstallationId);
        var replayAuthority = state.Authorities.FirstOrDefault(candidate =>
            string.Equals(candidate.RollbackReplay?.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal));
        if (replayAuthority?.RollbackReplay is { } replay)
        {
            if (!Matches(replay, command))
                throw new FeatureConcurrencyException("The rollback idempotency id is already bound to another command.");
            DemandRollbackReplay(state, replayAuthority, replay);
            return state;
        }
        DemandRevision(state, command.ExpectedRevision);
        var index = AuthorityIndex(state, command.InstallationId);
        var authority = state.Authorities[index];
        if (authority.ActiveRelease != command.ExpectedActiveRelease || authority.Paused ||
            authority.PendingRelease is not null || authority.PendingGrantRevision is not null || authority.PendingGrants.Length != 0)
            throw new FeatureConcurrencyException(
                "The active installation does not match the rollback command.",
                FeatureCommandRejectionReason.Precondition);
        if (authority.PreviousRelease != command.TargetRelease || authority.PreviousGrantRevision is not { } previousRevision ||
            authority.PreviousSubscriptions is not { } previousSubscriptions)
            throw new FeatureConcurrencyException(
                "The exact rollback target is not available.",
                FeatureCommandRejectionReason.Precondition);
        var registrationIndex = Array.FindIndex(
            state.Installations,
            candidate => candidate.InstallationId == command.InstallationId);
        if (registrationIndex < 0 || state.Installations[registrationIndex].Release != command.ExpectedActiveRelease)
            throw new FeatureConcurrencyException(
                "The active installation registration does not match the rollback command.",
                FeatureCommandRejectionReason.Precondition);
        var restoredRegistration = new FeatureInstallationRegistration(
            command.InstallationId,
            command.TargetRelease,
            previousSubscriptions.ToArray());
        var resultAccessDigest = FeaturePublicationTransitions.AccessDigest(
            command.InstallationId,
            command.TargetRelease,
            authority.PreviousGrants,
            restoredRegistration.Subscriptions);
        var authorities = state.Authorities.ToArray();
        authorities[index] = FeaturePublicationTransitions.Invalidate(authority with
        {
            ActiveRelease = command.TargetRelease,
            ActiveGrantRevision = previousRevision,
            ActiveGrants = authority.PreviousGrants,
            PreviousRelease = null,
            PreviousGrantRevision = null,
            PreviousGrants = [],
            PreviousSubscriptions = null,
            RollbackReplay = new FeatureRollbackReplay(
                command.InstallationId,
                command.ExpectedActiveRelease,
                command.TargetRelease,
                command.ExpectedRevision,
                command.IdempotencyId,
                resultAccessDigest)
        });
        var installations = state.Installations.ToArray();
        installations[registrationIndex] = restoredRegistration;
        return FeatureHubEvidenceLedger.NormalizeLifecycleEvidence(state with
        {
            Authorities = authorities,
            Installations = installations,
            Revision = checked(state.Revision + 1)
        }, command.InstallationId);
    }
    internal static bool ExactRollbackAvailable(FeatureInstallationAuthorityState authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (authority.ActiveRelease is not { } activeRelease || authority.ActiveGrantRevision is not { } activeRevision ||
            authority.PreviousRelease is not { } previousRelease || previousRelease == activeRelease ||
            authority.PreviousGrantRevision is not { } previousRevision || previousRevision.Value < 1 ||
            previousRevision.Value >= activeRevision.Value || authority.Paused || authority.PendingRelease is not null ||
            authority.PendingGrantRevision is not null || authority.PendingGrants is not { Length: 0 } ||
            authority.PreviousSubscriptions is not { } previousSubscriptions ||
            !CanonicalSubscriptions(previousSubscriptions) || authority.PreviousGrants is not { } previousGrants ||
            previousGrants.Any(static grant => grant is null))
            return false;
        try
        {
            var validated = ValidateGrants(previousGrants.Select(static grant => new FeatureGrantSpec(
                grant.CapabilityId,
                grant.CapabilityVersion,
                grant.ProviderConnectionId,
                grant.ConstraintsJson,
                grant.Provider)).ToArray());
            return SameGrants(previousGrants, validated);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
    public static FeatureGrantState? ReadGrant(FeatureHubState state, FeatureGrantLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(lookup);
        var authority = state.Authorities.FirstOrDefault(candidate =>
            candidate.InstallationId == lookup.InstallationId);
        if (authority is null || authority.Paused) return null;
        if (authority.ActiveRelease == lookup.Release)
            return FindGrant(authority.ActiveGrants, lookup);
        return authority.PreviousRelease == lookup.Release ? FindGrant(authority.PreviousGrants, lookup) : null;
    }
    public static FeatureHubState Register(FeatureHubState state, FeatureInstallationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.InstallationId.Value) || string.IsNullOrWhiteSpace(registration.Release.Value))
            throw new ArgumentException("A complete feature installation registration is required.", nameof(registration));
        ArgumentNullException.ThrowIfNull(registration.Subscriptions);
        if (!CanonicalSubscriptions(registration.Subscriptions))
            throw new ArgumentException("Canonical unique feature subscriptions are required.", nameof(registration));
        registration = registration with
        {
            Subscriptions = registration.Subscriptions.Order(StringComparer.Ordinal).ToArray()
        };
        var existing = Array.FindIndex(
            state.Installations,
            candidate => candidate.InstallationId == registration.InstallationId);
        if (existing >= 0)
        {
            var current = state.Installations[existing];
            if (current.Release == registration.Release &&
                current.Subscriptions.SequenceEqual(registration.Subscriptions, StringComparer.Ordinal))
                return state;
            var replaced = state.Installations.ToArray();
            replaced[existing] = registration;
            return WithRegistrationChange(state, replaced, registration.InstallationId);
        }
        FeatureHubEvidenceLedger.DemandOwnerCoordinateCapacity(state, registration.InstallationId);
        return WithRegistrationChange(state, [.. state.Installations, registration], registration.InstallationId);
    }
    public static FeatureCreateDraftTransition CreateDraft(FeatureHubState state, string ownerScope, CreateFeatureDraft request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerScope);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        if (request.OperationId.Length > FeatureLimits.DraftOperationIdCharacters || request.OperationId.Any(char.IsControl))
            throw new ArgumentException("A bounded canonical operation id is required.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Goal);
        if (request.Goal.Length > FeatureLimits.DraftGoalCharacters || request.Goal.Any(char.IsControl))
            throw new ArgumentException("A bounded control-character-free feature draft goal is required.", nameof(request));
        var legacyMissingConversation = request.ConversationId is null;
        var conversationId = legacyMissingConversation ? FeatureDraft.LegacyMissingConversationId : request.ConversationId!;
        if (!legacyMissingConversation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
            if (conversationId.Length > FeatureLimits.DraftConversationIdCharacters || conversationId.Any(char.IsControl))
                throw new ArgumentException("A bounded canonical conversation id is required.", nameof(request));
        }
        if (request.RequestedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature draft timestamps must be UTC.", nameof(request));
        var drafts = state.Drafts ?? [];
        var existing = drafts.FirstOrDefault(draft => string.Equals(draft.OriginatingRequest.OperationId, request.OperationId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(existing.Goal, request.Goal, StringComparison.Ordinal) ||
                existing.OriginatingRequest.ConversationId != FeatureDraft.LegacyMissingConversationId &&
                !string.Equals(existing.OriginatingRequest.ConversationId, conversationId, StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The operation id is already bound to a different feature draft goal.");
            return new FeatureCreateDraftTransition(state, existing);
        }
        if (drafts.Length >= FeatureLimits.DraftsPerOwner)
            throw new FeatureLimitExceededException("An owner can have at most 100 feature drafts.");
        var goal = request.Goal.Trim();
        if (ConstrainedFeaturePackTemplates.TryMatchEnrichSalesforce(goal))
            goal = ConstrainedFeaturePackTemplates.EnrichSalesforceAccountFromGmail;
        var draft = new FeatureDraft(
            new FeatureDraftId(DraftProposalId(ownerScope, request.OperationId)),
            new OriginatingRequest(request.OperationId, conversationId, goal),
            goal,
            "draft",
            ConstrainedFeaturePackTemplates.SeedBehavior(goal),
            ConstrainedFeaturePackTemplates.SeedSource(goal),
            null,
            null,
            0,
            request.RequestedAt,
            request.RequestedAt);
        FeatureDraft[] nextDrafts = [.. drafts, draft];
        FeatureDraftAuthoringTransitions.DemandOwnerDraftBudget(nextDrafts);
        var nextState = state with { Drafts = nextDrafts, Revision = checked(state.Revision + 1) };
        return new FeatureCreateDraftTransition(nextState, draft);
    }
    public static FeatureHubState BeginFanOut(FeatureHubState state, FeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        FeatureInstallationTransitions.ValidateInput(input);
        var existing = state.FanOuts.FirstOrDefault(batch =>
            string.Equals(batch.Input.InputId, input.InputId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(FeatureInstallationTransitions.InputDigest(existing.Input), FeatureInstallationTransitions.InputDigest(input), StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The fan-out input id is already bound to different content.");
            return state;
        }
        var deliveries = state.Installations.Where(registration => registration.Subscriptions.Any(
                subscription => string.Equals(subscription, input.Kind, StringComparison.Ordinal)))
            .Select(registration => new FeatureFanOutDeliveryState(registration.InstallationId, false))
            .ToArray();
        var batch = new FeatureFanOutState(input, deliveries);
        var retained = state.FanOuts;
        if (retained.Length >= FeatureLimits.FanOutBatches)
        {
            var completedIndex = Array.FindIndex(
                retained,
                candidate => candidate.Deliveries.All(delivery => delivery.Delivered));
            if (completedIndex < 0)
                throw new FeatureLimitExceededException("Pending feature fan-out exceeds the durable ledger capacity.");
            retained = retained.Where((_, index) => index != completedIndex).ToArray();
        }
        FeatureFanOutState[] fanOuts = [.. retained, batch];
        return state with { FanOuts = fanOuts, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState RecordDeliveries(FeatureHubState state, string inputId, IReadOnlySet<FeatureInstallationId> delivered)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId);
        ArgumentNullException.ThrowIfNull(delivered);
        var index = Array.FindIndex(
            state.FanOuts,
            batch => string.Equals(batch.Input.InputId, inputId, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException("The feature fan-out batch does not exist.");
        var batch = state.FanOuts[index];
        var deliveries = batch.Deliveries.Select(delivery =>
            delivery.Delivered || delivered.Contains(delivery.InstallationId) ? delivery with { Delivered = true } : delivery).ToArray();
        if (deliveries.SequenceEqual(batch.Deliveries))
            return state;
        var fanOuts = state.FanOuts.ToArray();
        fanOuts[index] = batch with { Deliveries = deliveries };
        return state with { FanOuts = fanOuts, Revision = checked(state.Revision + 1) };
    }
    public static FeatureHubState RecordDeliveryOutcomes(FeatureHubState state, string inputId, IReadOnlyList<FeatureDeliveryAttempt> attempts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        if (now.Offset != TimeSpan.Zero)
            throw new ArgumentException("Feature delivery timestamps must be UTC.", nameof(now));
        var delivered = attempts.Where(attempt => attempt.Status is FeatureAppendStatus.Accepted or FeatureAppendStatus.Duplicate)
            .Select(attempt => attempt.InstallationId)
            .ToHashSet();
        var next = RecordDeliveries(state, inputId, delivered);
        var full = attempts.Where(attempt => attempt.Status == FeatureAppendStatus.Full)
            .Select(attempt => attempt.InstallationId)
            .Distinct()
            .ToArray();
        if (full.Length == 0) return next;
        var batch = next.FanOuts.Single(candidate =>
            string.Equals(candidate.Input.InputId, inputId, StringComparison.Ordinal));
        var alerts = next.Alerts.ToList();
        foreach (var installationId in full)
        {
            if (alerts.Any(alert => alert.InstallationId == installationId && string.Equals(alert.InputId, inputId, StringComparison.Ordinal)))
                continue;
            alerts.Add(new FeatureBackpressureAlert(installationId, inputId, batch.Input.Kind, now, "feature inbox full"));
        }
        if (alerts.Count > FeatureLimits.FanOutBatches)
            alerts = alerts.TakeLast(FeatureLimits.FanOutBatches).ToList();
        var authorities = next.Authorities.Select(authority =>
            full.Contains(authority.InstallationId) &&
            !HasInstallationReservationOrReset(next, authority.InstallationId) &&
            (!authority.Paused || !string.Equals(authority.PauseReason, "feature inbox full", StringComparison.Ordinal))
                ? FeaturePublicationTransitions.Invalidate(authority with { Paused = true, PauseReason = "feature inbox full" })
                : authority).ToArray();
        return next with { Alerts = alerts.ToArray(), Authorities = authorities, Revision = checked(next.Revision + 1) };
    }
    private static FeatureReleaseMetadata ValidateRelease(FeatureReleaseMetadata release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Digest.Value is not { Length: 64 } releaseDigest ||
            releaseDigest.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A canonical release digest is required.", nameof(release));
        if (release.SourceReference is not { Length: 71 } sourceReference ||
            !sourceReference.StartsWith("sha256:", StringComparison.Ordinal) ||
            sourceReference[7..].Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("A content-addressed source reference is required.", nameof(release));
        var source = release.Source is null
            ? null
            : FeatureDraftAuthoringTransitions.ValidateSource(release.Source);
        if (source is not null && !string.Equals(
                release.SourceReference,
                FeatureDraftAuthoringTransitions.SourceReference(source),
                StringComparison.Ordinal))
            throw new ArgumentException("The source reference must exactly bind the embedded Source Snapshot.", nameof(release));
        var capabilities = CanonicalValues(release.RequestedCapabilities, "capability");
        var dependencies = CanonicalValues(release.Dependencies, "dependency");
        return release with { RequestedCapabilities = capabilities, Dependencies = dependencies, Source = source };
    }
    internal static FeatureGrantState[] ValidateGrants(FeatureGrantSpec[] grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Length > 32)
            throw new ArgumentException("A release cannot request more than 32 capabilities.", nameof(grants));
        var seen = new HashSet<(string, int)>();
        return grants.Select(grant =>
        {
            ArgumentNullException.ThrowIfNull(grant);
            if (string.IsNullOrWhiteSpace(grant.CapabilityId) || grant.CapabilityId.Length > 256 || grant.CapabilityId.Any(char.IsControl) || grant.CapabilityVersion < 1 ||
                !seen.Add((grant.CapabilityId, grant.CapabilityVersion)))
                throw new ArgumentException("Canonical unique capability grants are required.", nameof(grants));
            if (Encoding.UTF8.GetByteCount(grant.ConstraintsJson) > 65_536)
                throw new ArgumentException("Capability constraints exceed 64 KiB.", nameof(grants));
            try
            {
                using var document = JsonDocument.Parse(grant.ConstraintsJson);
                var constraints = CapabilityGrantConstraintPolicy.CopyValidated(document.RootElement);
                if (!CapabilityGrantConstraintPolicy.AllowsTool(constraints, grant.CapabilityId))
                    throw new ArgumentException("Capability constraints must allow the exact granted capability.", nameof(grants));
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Capability constraints must be a bounded JSON object.", nameof(grants), exception);
            }
            return new FeatureGrantState(
                grant.CapabilityId,
                grant.CapabilityVersion,
                grant.ProviderConnectionId,
                grant.ConstraintsJson,
                ValidateProvider(grant.Provider, grant.ProviderConnectionId));
        }).OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
            .ThenBy(grant => grant.CapabilityVersion)
            .ToArray();
    }
    private static bool CanonicalSubscriptions(IReadOnlyList<string> subscriptions) =>
        subscriptions.Count > 0 &&
        !subscriptions.Any(subscription =>
            string.IsNullOrWhiteSpace(subscription) || subscription.Length > 256 || subscription.Any(char.IsControl) ||
            !string.Equals(subscription, subscription.Trim(), StringComparison.Ordinal)) &&
        subscriptions.Distinct(StringComparer.Ordinal).Count() == subscriptions.Count;
    internal static bool SameGrants(IReadOnlyList<FeatureGrantState> left, IReadOnlyList<FeatureGrantState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.CapabilityId == pair.Second.CapabilityId && pair.First.CapabilityVersion == pair.Second.CapabilityVersion &&
            pair.First.ProviderConnectionId == pair.Second.ProviderConnectionId &&
            string.Equals(pair.First.ConstraintsJson, pair.Second.ConstraintsJson, StringComparison.Ordinal) &&
            string.Equals(pair.First.Provider, pair.Second.Provider, StringComparison.Ordinal));
    internal static bool SameRelease(FeatureReleaseMetadata left, FeatureReleaseMetadata right) =>
        left.Digest == right.Digest && string.Equals(left.SourceReference, right.SourceReference, StringComparison.Ordinal) &&
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
    private static string? ValidateProvider(string? provider, ProviderConnectionId? connection)
    {
        if (provider is null)
        {
            if (connection is not null)
                throw new ArgumentException("A provider key is required for a provider connection.", nameof(provider));
            return null;
        }
        if (string.IsNullOrWhiteSpace(provider) || provider.Length > 64 || provider.Any(char.IsControl) ||
            !string.Equals(provider, provider.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical provider key is required.", nameof(provider));
        return provider;
    }
    private static string[] CanonicalValues(string[] values, string kind)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length > 64 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) || !string.Equals(value, value.Trim(), StringComparison.Ordinal)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException($"Canonical unique {kind} identifiers are required.", nameof(values));
        return values.Order(StringComparer.Ordinal).ToArray();
    }

    private static string ApprovalId(FeatureInstallationId installationId, ReleaseDigest release, long revision) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"digitalbrain.v3.feature-approval\0{installationId.Value}\0{release.Value}\0{revision}")));
    private static string DraftProposalId(string ownerScope, string operationId) =>
        "proposal-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ownerScope + "\0" + operationId)))[..32];
    private static void DemandRevision(FeatureHubState state, long expectedRevision)
    {
        if (state.Revision != expectedRevision)
            throw new FeatureConcurrencyException("The feature hub revision changed.");
    }
    private static int AuthorityIndex(FeatureHubState state, FeatureInstallationId installationId)
    {
        var index = Array.FindIndex(state.Authorities, candidate => candidate.InstallationId == installationId);
        return index >= 0 ? index : throw new KeyNotFoundException("The feature installation authority does not exist.");
    }
    private static void DemandExactReservationProposal(
        FeatureHubState state,
        FeatureInstallationId installationId,
        ReleaseDigest release,
        FeatureGrantState[] grants)
    {
        var reservation = InstallationReservation(state, installationId);
        if (reservation is null) return;
        var reservedGrants = ReservedGrants(reservation);
        if (reservation.Release != release || !SameGrants(reservedGrants, grants))
            throw new FeatureConcurrencyException(
                "The Feature proposal does not match the exact installation reservation.",
                FeatureCommandRejectionReason.Precondition);
    }
    private static void DemandExactReservationDecision(
        FeatureHubState state,
        FeatureApprovalState approval,
        FeatureApprovalDecision decision)
    {
        var reservation = InstallationReservation(state, approval.InstallationId);
        if (reservation is null) return;
        if (decision.ActorId != reservation.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (approval.Release.Digest != reservation.Release ||
            !SameGrants(approval.Grants, ReservedGrants(reservation)) ||
            !string.Equals(decision.DecisionId, reservation.DecisionId, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The Feature decision does not match the exact installation reservation.",
                FeatureCommandRejectionReason.Precondition);
    }
    private static void DemandExactReservationGrant(
        FeatureHubState state,
        FeatureGrantRequest request,
        FeatureGrantState[] grants)
    {
        var reservation = InstallationReservation(state, request.InstallationId);
        if (reservation is null) return;
        if (request.ActorId != reservation.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (request.Release != reservation.Release || !SameGrants(grants, ReservedGrants(reservation)))
            throw new FeatureConcurrencyException(
                "The Feature grant does not match the exact installation reservation.",
                FeatureCommandRejectionReason.Precondition);
        var approvals = state.Approvals.Where(candidate =>
            candidate.InstallationId == reservation.InstallationId &&
            candidate.Release.Digest == reservation.Release &&
            candidate.Status == FeatureApprovalStatus.Approved &&
            string.Equals(candidate.DecisionId, reservation.DecisionId, StringComparison.Ordinal) &&
            candidate.DecisionActorId == reservation.ActorId &&
            SameGrants(candidate.Grants, grants)).ToArray();
        if (approvals.Length != 1)
            throw new FeatureConcurrencyException(
                "The Feature grant is not bound to the reserved actor decision.",
                FeatureCommandRejectionReason.Precondition);
    }
    private static void DemandExactReservationActivation(
        FeatureHubState state,
        FeatureInstallationAuthorityState authority)
    {
        var reservation = InstallationReservation(state, authority.InstallationId);
        if (reservation is null) return;
        if (authority.ActorId != reservation.ActorId)
            throw new FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason.ActorMismatch);
        if (authority.PendingRelease != reservation.Release ||
            !SameGrants(authority.PendingGrants, ReservedGrants(reservation)))
            throw new FeatureConcurrencyException(
                "The pending Feature authority does not match the exact installation reservation.",
                FeatureCommandRejectionReason.Precondition);
    }
    internal static void DemandExactReservedInstallation(
        FeatureHubState state,
        FeatureInstallationRegistration registration)
    {
        var reservation = InstallationReservation(state, registration.InstallationId);
        if (reservation is null) return;
        if (reservation.Subscriptions is null || registration.Release != reservation.Release ||
            !registration.Subscriptions.Order(StringComparer.Ordinal)
                .SequenceEqual(reservation.Subscriptions, StringComparer.Ordinal))
            throw new FeatureConcurrencyException(
                "The Feature registration does not match the exact installation reservation.",
                FeatureCommandRejectionReason.Precondition);
    }
    private static FeatureDraftInstallationReservation? InstallationReservation(
        FeatureHubState state,
        FeatureInstallationId installationId)
    {
        var reservations = (state.DraftInstallationReservations ?? [])
            .Where(candidate => candidate.InstallationId == installationId)
            .ToArray();
        if (reservations.Length > 1)
            throw new FeatureConcurrencyException(
                "The Feature installation has ambiguous reservations.",
                FeatureCommandRejectionReason.Precondition);
        return reservations.SingleOrDefault();
    }
    private static FeatureGrantState[] ReservedGrants(FeatureDraftInstallationReservation reservation)
    {
        try
        {
            return ValidateGrants(reservation.Grants
                ?? throw new FeatureConcurrencyException(
                    "The Feature installation reservation has no exact grant plan.",
                    FeatureCommandRejectionReason.Precondition));
        }
        catch (ArgumentException)
        {
            throw new FeatureConcurrencyException(
                "The Feature installation reservation grant plan is invalid.",
                FeatureCommandRejectionReason.Precondition);
        }
    }
    private static void DemandNoInstallationReset(FeatureHubState state, FeatureInstallationId installationId)
    {
        if ((state.DraftInstallationResets ?? []).Any(candidate => candidate.InstallationId == installationId))
            throw new FeatureConcurrencyException(
                "The Feature installation has a reset in progress.",
                FeatureCommandRejectionReason.Precondition);
    }
    internal static void DemandNoInstallationReservationOrReset(
        FeatureHubState state,
        FeatureInstallationId installationId)
    {
        if (HasInstallationReservationOrReset(state, installationId))
            throw new FeatureConcurrencyException(
                "The Feature installation has an authoring reservation or reset in progress.",
                FeatureCommandRejectionReason.Precondition);
    }
    internal static bool HasInstallationReservationOrReset(
        FeatureHubState state,
        FeatureInstallationId installationId) =>
        (state.DraftInstallationReservations ?? []).Any(candidate => candidate.InstallationId == installationId) ||
        (state.DraftInstallationResets ?? []).Any(candidate => candidate.InstallationId == installationId);
    private static FeatureGrantState? FindGrant(FeatureGrantState[] grants, FeatureGrantLookup lookup) =>
        grants.FirstOrDefault(grant =>
            string.Equals(grant.CapabilityId, lookup.CapabilityId, StringComparison.Ordinal) &&
            grant.CapabilityVersion == lookup.CapabilityVersion);
    private static void DemandIdempotencyId(string idempotencyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyId);
        if (idempotencyId.Length > 256 || idempotencyId.Any(char.IsControl) ||
            !string.Equals(idempotencyId, idempotencyId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical idempotency id is required.", nameof(idempotencyId));
    }
    private static void DemandRollbackReplay(
        FeatureHubState state,
        FeatureInstallationAuthorityState authority,
        FeatureRollbackReplay replay)
    {
        var registration = state.Installations.SingleOrDefault(candidate => candidate.InstallationId == replay.InstallationId);
        if (authority.ActiveRelease != replay.TargetRelease || authority.ActiveGrantRevision is null || authority.Paused ||
            authority.PendingRelease is not null || authority.PendingGrantRevision is not null || authority.PendingGrants.Length != 0 ||
            registration is null || registration.Release != replay.TargetRelease ||
            !string.Equals(
                FeaturePublicationTransitions.AccessDigest(
                    replay.InstallationId,
                    replay.TargetRelease,
                    authority.ActiveGrants,
                    registration.Subscriptions),
                replay.ResultAccessDigest,
                StringComparison.Ordinal))
            throw new FeatureConcurrencyException("The completed rollback result is no longer current.");
    }
    private static bool Matches(FeatureRollbackReplay replay, RollbackFeatureInstallation command) =>
        replay.InstallationId == command.InstallationId &&
        replay.ExpectedActiveRelease == command.ExpectedActiveRelease && replay.TargetRelease == command.TargetRelease &&
        replay.ExpectedRevision == command.ExpectedRevision &&
        string.Equals(replay.IdempotencyId, command.IdempotencyId, StringComparison.Ordinal);
    private static FeatureInstallationAuthorityState RemoveGrant(FeatureInstallationAuthorityState authority, FeatureGrantRevocation revocation)
    {
        var active = authority.ActiveRelease == revocation.Release
            ? authority.ActiveGrants.Where(grant => !Matches(grant, revocation)).ToArray()
            : authority.ActiveGrants;
        var previous = authority.PreviousRelease == revocation.Release
            ? authority.PreviousGrants.Where(grant => !Matches(grant, revocation)).ToArray()
            : authority.PreviousGrants;
        if (active.Length == authority.ActiveGrants.Length && previous.Length == authority.PreviousGrants.Length)
            return authority;
        var nextRevision = new GrantRevision(checked(new[]
        {
            authority.ActiveGrantRevision?.Value ?? 0,
            authority.PreviousGrantRevision?.Value ?? 0,
            authority.PendingGrantRevision?.Value ?? 0
        }.Max() + 1));
        return authority with
        {
            ActiveGrants = active,
            PreviousGrants = previous,
            ActiveGrantRevision = authority.ActiveRelease == revocation.Release ? nextRevision : authority.ActiveGrantRevision,
            PreviousGrantRevision = authority.PreviousRelease == revocation.Release ? nextRevision : authority.PreviousGrantRevision
        };
    }
    private static bool Matches(FeatureGrantState grant, FeatureGrantRevocation revocation) =>
        string.Equals(grant.CapabilityId, revocation.CapabilityId, StringComparison.Ordinal) &&
        grant.CapabilityVersion == revocation.CapabilityVersion;

    private static FeatureHubState WithRegistrationChange(
        FeatureHubState state,
        FeatureInstallationRegistration[] installations,
        FeatureInstallationId installationId)
    {
        var authorityIndex = Array.FindIndex(state.Authorities, candidate => candidate.InstallationId == installationId);
        if (authorityIndex < 0)
            return state with { Installations = installations, Revision = checked(state.Revision + 1) };
        var authorities = state.Authorities.ToArray();
        authorities[authorityIndex] = FeaturePublicationTransitions.Invalidate(authorities[authorityIndex]);
        return state with
        {
            Installations = installations,
            Authorities = authorities,
            Revision = checked(state.Revision + 1)
        };
    }
}
