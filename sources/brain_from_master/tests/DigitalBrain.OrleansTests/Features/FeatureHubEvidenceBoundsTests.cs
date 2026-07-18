using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureHubEvidenceBoundsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly ActorId Actor = new("actor-evidence-bounds");
    private static readonly FeatureInstallationId InstallationId = new("installation-evidence-bounds");

    [Fact]
    public void Seventy_activations_retain_only_live_release_evidence_and_exact_rollback_prunes_the_rolled_away_release()
    {
        var state = FeatureHubState.Empty;
        for (var index = 0; index < 70; index++)
            state = ActivateAndRegister(state, Digest(index + 1), index);

        var authority = Assert.Single(state.Authorities);
        var activeRelease = Assert.IsType<ReleaseDigest>(authority.ActiveRelease);
        var previousRelease = Assert.IsType<ReleaseDigest>(authority.PreviousRelease);
        var currentApprovals = state.Approvals
            .Where(candidate => candidate.Status != FeatureApprovalStatus.Superseded)
            .ToArray();

        Assert.True(state.Approvals.Length <= FeatureLimits.ApprovalLedgerRecords);
        Assert.Equal(2, currentApprovals.Length);
        Assert.All(currentApprovals, approval =>
            Assert.Contains(approval.Release.Digest, new[] { activeRelease, previousRelease }));
        Assert.Equal([previousRelease, activeRelease], state.Releases.Select(release => release.Digest));
        Assert.True(FeatureHubTransitions.ExactRollbackAvailable(authority));

        var rolledBack = FeatureHubTransitions.RollbackAuthority(
            state,
            new RollbackFeatureInstallation(
                InstallationId,
                activeRelease,
                previousRelease,
                state.Revision,
                "rollback-evidence-bounds"));

        var restored = Assert.Single(rolledBack.Authorities);
        Assert.Equal(previousRelease, restored.ActiveRelease);
        Assert.Null(restored.PreviousRelease);
        Assert.Equal(previousRelease, Assert.Single(rolledBack.Releases).Digest);
        Assert.DoesNotContain(
            rolledBack.Approvals,
            approval => approval.Status != FeatureApprovalStatus.Superseded &&
                approval.Release.Digest == activeRelease);
    }

    [Fact]
    public void Pending_or_approved_unbound_candidate_rejects_a_second_candidate_without_mutation()
    {
        var firstRelease = Digest(101);
        var secondRelease = Digest(102);
        var pending = FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(InstallationId, firstRelease),
            0,
            Now);

        AssertRejectedWithoutMutation(pending, Proposal(InstallationId, secondRelease));

        var approval = Assert.Single(pending.Approvals);
        var approved = FeatureHubTransitions.Decide(
            pending,
            new FeatureApprovalDecision(
                approval.ApprovalId,
                firstRelease,
                true,
                "decision-approved-unbound",
                Actor),
            pending.Revision,
            Now.AddSeconds(1));

        AssertRejectedWithoutMutation(approved, Proposal(InstallationId, secondRelease));
    }

    [Fact]
    public void Staged_pending_authority_rejects_a_different_candidate_without_mutation()
    {
        var stagedRelease = Digest(105);
        var otherRelease = Digest(106);
        var proposed = FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(InstallationId, stagedRelease),
            0,
            Now);
        var approval = Assert.Single(proposed.Approvals);
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(
                approval.ApprovalId,
                stagedRelease,
                true,
                "decision-staged-candidate",
                Actor),
            proposed.Revision,
            Now.AddSeconds(1));
        var staged = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(InstallationId, stagedRelease, Actor, []),
            approved.Revision);

        AssertRejectedWithoutMutation(staged, Proposal(InstallationId, otherRelease));
    }

    [Fact]
    public void Activation_fails_closed_when_seeded_state_contains_an_obsolete_pending_candidate()
    {
        var stagedRelease = Digest(107);
        var obsoleteRelease = Digest(108);
        var proposed = FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(InstallationId, stagedRelease),
            0,
            Now);
        var approval = Assert.Single(proposed.Approvals);
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(
                approval.ApprovalId,
                stagedRelease,
                true,
                "decision-seeded-ambiguity",
                Actor),
            proposed.Revision,
            Now.AddSeconds(1));
        var staged = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(InstallationId, stagedRelease, Actor, []),
            approved.Revision);
        var ambiguous = staged with
        {
            Approvals =
            [
                .. staged.Approvals,
                new FeatureApprovalState(
                    "approval-seeded-obsolete-pending",
                    InstallationId,
                    Metadata(obsoleteRelease),
                    [],
                    [],
                    FeatureApprovalStatus.Pending,
                    null,
                    null,
                    staged.Revision,
                    [])
            ],
            Releases = [.. staged.Releases, Metadata(obsoleteRelease)]
        };
        var authorities = ambiguous.Authorities;
        var approvals = ambiguous.Approvals;

        Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureHubTransitions.Activate(
                ambiguous,
                InstallationId,
                ambiguous.Revision));

        Assert.Same(authorities, ambiguous.Authorities);
        Assert.Same(approvals, ambiguous.Approvals);
        Assert.Equal(staged.Revision, ambiguous.Revision);
    }

    [Fact]
    public void A_new_candidate_supersedes_rejected_unbound_evidence_and_prunes_its_release()
    {
        var rejectedRelease = Digest(111);
        var replacementRelease = Digest(112);
        var proposed = FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(InstallationId, rejectedRelease),
            0,
            Now);
        var approval = Assert.Single(proposed.Approvals);
        var rejected = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(
                approval.ApprovalId,
                rejectedRelease,
                false,
                "decision-rejected-unbound",
                Actor),
            proposed.Revision,
            Now.AddSeconds(1));

        var replaced = FeatureHubTransitions.Propose(
            rejected,
            Proposal(InstallationId, replacementRelease),
            rejected.Revision,
            Now.AddSeconds(2));

        Assert.Equal(
            FeatureApprovalStatus.Superseded,
            replaced.Approvals.Single(candidate => candidate.Release.Digest == rejectedRelease).Status);
        Assert.Equal(
            FeatureApprovalStatus.Pending,
            replaced.Approvals.Single(candidate => candidate.Release.Digest == replacementRelease).Status);
        Assert.Equal(replacementRelease, Assert.Single(replaced.Releases).Digest);
    }

    [Fact]
    public void One_hundred_staged_coordinates_reject_a_new_coordinate_without_mutation_and_allow_an_existing_coordinate()
    {
        var state = StateWithStagedCoordinates(FeatureLimits.InstallationsPerOwner);
        var newInstallation = new FeatureInstallationId("installation-capacity-new");
        var newRelease = Digest(1001);

        AssertCapacityProposalRejectedWithoutMutation(state, Proposal(newInstallation, newRelease));
        AssertRegisterRejectedWithoutMutation(
            state,
            new FeatureInstallationRegistration(newInstallation, newRelease, ["new-event"]));

        var existingApproval = state.Approvals[23];
        Assert.Same(
            state,
            FeatureHubTransitions.Propose(
                state,
                Proposal(existingApproval.InstallationId, existingApproval.Release.Digest),
                state.Revision,
                Now));
        var decided = FeatureHubTransitions.Decide(
            state,
            new FeatureApprovalDecision(
                existingApproval.ApprovalId,
                existingApproval.Release.Digest,
                true,
                "decision-existing-coordinate",
                Actor),
            state.Revision,
            Now.AddSeconds(1));
        var granted = FeatureHubTransitions.Grant(
            decided,
            new FeatureGrantRequest(
                existingApproval.InstallationId,
                existingApproval.Release.Digest,
                Actor,
                []),
            decided.Revision);
        Assert.Equal(existingApproval.InstallationId, Assert.Single(granted.Authorities).InstallationId);
        AssertCapacityProposalRejectedWithoutMutation(
            granted,
            Proposal(newInstallation, newRelease));
        var registered = FeatureHubTransitions.Register(
            state,
            new FeatureInstallationRegistration(
                existingApproval.InstallationId,
                existingApproval.Release.Digest,
                ["existing-event"]));

        Assert.Equal(existingApproval.InstallationId, Assert.Single(registered.Installations).InstallationId);
    }

    [Fact]
    public void Reservation_admission_rejects_a_101st_coordinate_and_allows_an_existing_staged_coordinate()
    {
        var staged = StateWithStagedCoordinates(FeatureLimits.InstallationsPerOwner);
        var release = Digest(1101);
        var created = FeatureHubTransitions.CreateDraft(
            staged,
            "owner-reservation-capacity",
            new CreateFeatureDraft(
                "operation-reservation-capacity",
                "Reserve bounded evidence",
                Now,
                "conversation-reservation-capacity"));
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                FeatureVerificationTestData.Passing(release, created.Draft.Source, 1, Now),
                created.Draft.Revision,
                "verification-reservation-capacity"));
        var newCommand = InstallationCommand(
            verified.Draft,
            new FeatureInstallationId("installation-reservation-capacity-new"),
            release,
            "install-reservation-capacity-new");
        var before = verified.State;
        var approvals = before.Approvals.ToArray();
        var reservations = before.DraftInstallationReservations;

        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureDraftAuthoringTransitions.AcquireInstallationReservation(before, newCommand, Actor));

        Assert.Equal(staged.Approvals.Length, before.Approvals.Length);
        Assert.Equal(approvals, before.Approvals);
        Assert.Same(reservations, before.DraftInstallationReservations);
        var existingCoordinate = staged.Approvals[31].InstallationId;
        var existing = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            before,
            InstallationCommand(
                verified.Draft,
                existingCoordinate,
                release,
                "install-reservation-capacity-existing"),
            Actor);
        Assert.Equal(existingCoordinate, existing.Reservation.InstallationId);
    }

    [Fact]
    public void Reservation_authority_baseline_keeps_previous_release_evidence_in_relative_order()
    {
        var previousRelease = Digest(1201);
        var activeRelease = Digest(1202);
        var candidateRelease = Digest(1203);
        var discardedRelease = Digest(1204);
        var baseline = new FeatureInstallationAuthorityBaseline(
            InstallationId,
            Actor,
            activeRelease,
            previousRelease,
            new GrantRevision(2),
            [],
            new GrantRevision(1),
            [],
            false,
            null,
            0,
            null,
            ["previous-event"],
            null,
            new FeatureInstallationRegistration(InstallationId, activeRelease, ["active-event"]));
        var reservation = new FeatureDraftInstallationReservation(
            new FeatureDraftId("draft-baseline-release-evidence"),
            1,
            InstallationId,
            candidateRelease,
            "install-baseline-release-evidence",
            new string('a', 64),
            new string('b', 64),
            "decision-baseline-release-evidence",
            Actor,
            [],
            ["candidate-event"],
            42,
            activeRelease,
            previousRelease,
            baseline);
        var state = FeatureHubState.Empty with
        {
            Releases =
            [
                Metadata(discardedRelease),
                Metadata(previousRelease),
                Metadata(activeRelease)
            ],
            DraftInstallationReservations = [reservation]
        };

        var proposed = FeatureHubTransitions.Propose(
            state,
            Proposal(InstallationId, candidateRelease),
            state.Revision,
            Now);

        Assert.Equal(
            [previousRelease, activeRelease, candidateRelease],
            proposed.Releases.Select(release => release.Digest));
    }

    [Fact]
    public void Completed_reset_removes_candidate_release_evidence()
    {
        var activeRelease = Digest(1301);
        var candidateRelease = Digest(1302);
        var discardedRelease = Digest(1303);
        var activeRegistration = new FeatureInstallationRegistration(
            InstallationId,
            activeRelease,
            ["active-event"]);
        var activeAuthority = new FeatureInstallationAuthorityState(
            InstallationId,
            Actor,
            activeRelease,
            null,
            new GrantRevision(1),
            [],
            null,
            [],
            null,
            null,
            [],
            true,
            "operator pause");
        var initial = FeatureHubState.Empty with
        {
            Installations = [activeRegistration],
            Authorities = [activeAuthority],
            Releases = [Metadata(discardedRelease), Metadata(activeRelease)]
        };
        var created = FeatureHubTransitions.CreateDraft(
            initial,
            "owner-reset-release-evidence",
            new CreateFeatureDraft(
                "operation-reset-release-evidence",
                "Reset candidate evidence",
                Now,
                "conversation-reset-release-evidence"));
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                FeatureVerificationTestData.Passing(candidateRelease, created.Draft.Source, 1, Now),
                created.Draft.Revision,
                "verification-reset-release-evidence"));
        var command = new InstallFeatureVersion(
            verified.Draft.DraftId,
            verified.Draft.Revision,
            InstallationId,
            candidateRelease,
            [],
            ["candidate-event"],
            "decision-reset-release-evidence",
            "install-reset-release-evidence",
            42,
            activeRelease,
            null);
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            command,
            Actor);
        var proposed = FeatureHubTransitions.Propose(
            reserved.State,
            Proposal(InstallationId, candidateRelease),
            reserved.State.Revision,
            Now);
        var approval = proposed.Approvals.Single(candidate =>
            candidate.Release.Digest == candidateRelease && candidate.Status == FeatureApprovalStatus.Pending);
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(
                approval.ApprovalId,
                candidateRelease,
                true,
                command.DecisionId,
                Actor),
            proposed.Revision,
            Now.AddSeconds(1));
        var staged = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(InstallationId, candidateRelease, Actor, []),
            approved.Revision);

        var reset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            staged,
            new ResetFeatureDraftInstallationReservation(
                command.DraftId,
                "reset-release-evidence",
                command),
            Actor,
            Now.AddSeconds(2));

        Assert.True(reset.Completed);
        Assert.False(reset.RequiresRepublish);
        Assert.Empty(reset.State.DraftInstallationReservations ?? []);
        Assert.Equal(activeRelease, Assert.Single(reset.State.Releases).Digest);
    }

    private static FeatureHubState ActivateAndRegister(
        FeatureHubState state,
        ReleaseDigest release,
        int index)
    {
        var proposed = FeatureHubTransitions.Propose(
            state,
            Proposal(InstallationId, release),
            state.Revision,
            Now.AddMinutes(index));
        var approval = proposed.Approvals.Single(candidate =>
            candidate.Release.Digest == release && candidate.Status == FeatureApprovalStatus.Pending);
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(
                approval.ApprovalId,
                release,
                true,
                $"decision-activation-{index}",
                Actor),
            proposed.Revision,
            Now.AddMinutes(index).AddSeconds(1));
        var granted = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(InstallationId, release, Actor, []),
            approved.Revision);
        var activated = FeatureHubTransitions.Activate(granted, InstallationId, granted.Revision);
        return FeatureHubTransitions.Register(
            activated,
            new FeatureInstallationRegistration(InstallationId, release, [$"event-{index}"]));
    }

    private static FeatureHubState StateWithStagedCoordinates(int count)
    {
        var approvals = Enumerable.Range(0, count)
            .Select(index =>
            {
                var installationId = new FeatureInstallationId($"installation-staged-{index}");
                var release = Metadata(Digest(2000 + index));
                return new FeatureApprovalState(
                    $"approval-staged-{index}",
                    installationId,
                    release,
                    [],
                    [],
                    FeatureApprovalStatus.Pending,
                    null,
                    null,
                    index + 1,
                    []);
            })
            .ToArray();
        return FeatureHubState.Empty with
        {
            Approvals = approvals,
            Releases = approvals.Select(approval => approval.Release).ToArray(),
            Revision = count
        };
    }

    private static InstallFeatureVersion InstallationCommand(
        FeatureDraft draft,
        FeatureInstallationId installationId,
        ReleaseDigest release,
        string idempotencyId) => new(
        draft.DraftId,
        draft.Revision,
        installationId,
        release,
        [],
        ["manual"],
        "decision-reservation-capacity",
        idempotencyId);

    private static FeatureReleaseProposal Proposal(
        FeatureInstallationId installationId,
        ReleaseDigest release) => new(
        installationId,
        Metadata(release),
        []);

    private static FeatureReleaseMetadata Metadata(ReleaseDigest release) => new(
        release,
        "sha256:" + release.Value,
        FeatureSourceKind.Repository,
        [],
        ["DigitalBrain.Features.Sdk"]);

    private static ReleaseDigest Digest(int value) => new(value.ToString("x64"));

    private static void AssertRejectedWithoutMutation(
        FeatureHubState state,
        FeatureReleaseProposal proposal)
    {
        var revision = state.Revision;
        var releases = state.Releases;
        var approvals = state.Approvals;
        var releaseSnapshot = releases.ToArray();
        var approvalSnapshot = approvals.ToArray();

        Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureHubTransitions.Propose(state, proposal, state.Revision, Now));

        Assert.Equal(revision, state.Revision);
        Assert.Same(releases, state.Releases);
        Assert.Same(approvals, state.Approvals);
        Assert.Equal(releaseSnapshot, state.Releases);
        Assert.Equal(approvalSnapshot, state.Approvals);
    }

    private static void AssertCapacityProposalRejectedWithoutMutation(
        FeatureHubState state,
        FeatureReleaseProposal proposal)
    {
        var revision = state.Revision;
        var releases = state.Releases;
        var approvals = state.Approvals;
        var releaseSnapshot = releases.ToArray();
        var approvalSnapshot = approvals.ToArray();

        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureHubTransitions.Propose(state, proposal, state.Revision, Now));

        Assert.Equal(revision, state.Revision);
        Assert.Same(releases, state.Releases);
        Assert.Same(approvals, state.Approvals);
        Assert.Equal(releaseSnapshot, state.Releases);
        Assert.Equal(approvalSnapshot, state.Approvals);
    }

    private static void AssertRegisterRejectedWithoutMutation(
        FeatureHubState state,
        FeatureInstallationRegistration registration)
    {
        var revision = state.Revision;
        var installations = state.Installations;
        var snapshot = installations.ToArray();

        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureHubTransitions.Register(state, registration));

        Assert.Equal(revision, state.Revision);
        Assert.Same(installations, state.Installations);
        Assert.Equal(snapshot, state.Installations);
    }
}
