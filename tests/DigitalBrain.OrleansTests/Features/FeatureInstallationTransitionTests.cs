using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureInstallationTransitionTests
{
    private static readonly FeatureInstallationId InstallationId = new("installation-1");
    private static readonly ReleaseDigest ReleaseOne = new(new string('a', 64));
    private static readonly ReleaseDigest ReleaseTwo = new(new string('b', 64));
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Duplicate_input_is_acknowledged_without_growing_the_inbox()
    {
        var state = State();
        var first = FeatureInstallationTransitions.Append(state, Input("input-1"), Now);
        var duplicate = FeatureInstallationTransitions.Append(first.State, Input("input-1"), Now.AddSeconds(1));

        Assert.Equal(FeatureAppendStatus.Accepted, first.Status);
        Assert.Equal(FeatureAppendStatus.Duplicate, duplicate.Status);
        Assert.Single(duplicate.State.Inbox);
        Assert.Equal(first.State.Revision, duplicate.State.Revision);
    }

    [Fact]
    public void Reusing_an_input_id_with_different_content_is_rejected_before_and_after_completion()
    {
        var first = FeatureInstallationTransitions.Append(State(), Input("input-conflict"), Now);
        var conflicting = Input("input-conflict") with { PayloadJson = "{\"different\":true}" };

        Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureInstallationTransitions.Append(first.State, conflicting, Now));

        var claimed = FeatureInstallationTransitions.Claim(first.State, "host-1", Now, TimeSpan.FromSeconds(60));
        var committed = FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(Assert.IsType<FeatureRunClaim>(claimed.Claim).Fence),
            Now.AddSeconds(1));
        Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureInstallationTransitions.Append(committed.State, conflicting, Now.AddSeconds(2)));
    }

    [Fact]
    public void Expired_lease_can_be_reclaimed_and_the_old_fence_is_rejected()
    {
        var appended = FeatureInstallationTransitions.Append(State(), Input("input-1"), Now);
        var first = FeatureInstallationTransitions.Claim(appended.State, "host-1", Now, TimeSpan.FromSeconds(60));
        var beforeExpiry = FeatureInstallationTransitions.Claim(
            first.State,
            "host-2",
            Now.AddSeconds(59),
            TimeSpan.FromSeconds(60));
        var reclaimed = FeatureInstallationTransitions.Claim(
            first.State,
            "host-2",
            Now.AddSeconds(60),
            TimeSpan.FromSeconds(60));

        Assert.Null(beforeExpiry.Claim);
        Assert.NotNull(reclaimed.Claim);
        Assert.Equal(2, reclaimed.Claim.Attempt);
        Assert.True(reclaimed.Claim.Fence.Fence > first.Claim!.Fence.Fence);
        Assert.Throws<FeatureConcurrencyException>(() => FeatureInstallationTransitions.Commit(
            reclaimed.State,
            Commit(first.Claim.Fence),
            Now.AddSeconds(61)));
    }

    [Fact]
    public void Sixth_claim_after_consecutive_crashes_parks_and_pauses_instead_of_bypassing_the_attempt_limit()
    {
        var state = FeatureInstallationTransitions.Append(State(), Input("input-crashes"), Now).State;
        for (var attempt = 1; attempt <= FeatureLimits.AttemptsPerInput; attempt++)
        {
            var claimed = FeatureInstallationTransitions.Claim(
                state,
                $"host-{attempt}",
                Now.AddMinutes(attempt - 1),
                TimeSpan.FromSeconds(60));
            Assert.Equal(attempt, Assert.IsType<FeatureRunClaim>(claimed.Claim).Attempt);
            state = claimed.State;
        }

        var parked = FeatureInstallationTransitions.Claim(
            state,
            "host-overflow",
            Now.AddMinutes(FeatureLimits.AttemptsPerInput),
            TimeSpan.FromSeconds(60));

        Assert.Null(parked.Claim);
        Assert.True(parked.State.Paused);
        Assert.True(Assert.Single(parked.State.Inbox).Parked);
        Assert.Equal(FeatureLimits.AttemptsPerInput, parked.State.Inbox[0].Attempts);
    }

    [Fact]
    public void Idle_claim_is_a_no_op_but_clearing_an_expired_lease_advances_revision()
    {
        var state = State();
        var idle = FeatureInstallationTransitions.Claim(
            state,
            "host-idle",
            Now,
            TimeSpan.FromSeconds(60));
        Assert.Null(idle.Claim);
        Assert.Same(state, idle.State);
        Assert.Equal(0, idle.State.Revision);

        var claimed = Claimed();
        var unavailable = claimed.State with
        {
            Inbox = [claimed.State.Inbox[0] with { NotBefore = Now.AddHours(1) }]
        };
        var cleared = FeatureInstallationTransitions.Claim(
            unavailable,
            "host-after-expiry",
            claimed.Claim.LeaseExpiresAt,
            TimeSpan.FromSeconds(60));

        Assert.Null(cleared.Claim);
        Assert.Null(cleared.State.Lease);
        Assert.Equal(unavailable.Revision + 1, cleared.State.Revision);
    }

    [Fact]
    public void Expired_worker_cannot_fail_or_defer_its_input()
    {
        var claimed = Claimed();

        Assert.Throws<FeatureConcurrencyException>(() => FeatureInstallationTransitions.Fail(
            claimed.State,
            claimed.Claim.Fence,
            Now.AddSeconds(61),
            Now.AddSeconds(62),
            "late failure"));
    }

    [Fact]
    public void Lease_is_expired_for_fail_and_commit_at_the_exact_deadline()
    {
        var claimed = Claimed();
        var deadline = claimed.Claim.LeaseExpiresAt;

        Assert.Throws<FeatureConcurrencyException>(() => FeatureInstallationTransitions.Fail(
            claimed.State,
            claimed.Claim.Fence,
            deadline,
            deadline,
            "deadline failure"));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(claimed.Claim.Fence),
            deadline));
    }

    [Fact]
    public void Retrying_an_identical_commit_after_an_ambiguous_response_is_idempotent()
    {
        var claimed = Claimed();
        var commit = Commit(
            claimed.Claim.Fence,
            "{\"counter\":1}",
            [new FeatureIntent("notify", FeatureIntentKind.Event, "{\"value\":1}")]);
        var first = FeatureInstallationTransitions.Commit(claimed.State, commit, Now.AddSeconds(1));
        var retried = FeatureInstallationTransitions.Commit(first.State, commit, Now.AddSeconds(2));

        Assert.Equal(first.Completion, retried.Completion);
        Assert.Same(first.State, retried.State);
        Assert.Single(retried.State.Completions);
        Assert.Single(retried.State.Intents);
    }

    [Fact]
    public void A_conflicting_retry_for_a_completed_input_is_rejected()
    {
        var claimed = Claimed();
        var first = FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(claimed.Claim.Fence, "{\"counter\":1}"),
            Now.AddSeconds(1));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureInstallationTransitions.Commit(
            first.State,
            Commit(claimed.Claim.Fence, "{\"counter\":2}"),
            Now.AddSeconds(2)));
    }

    [Fact]
    public void A_stale_fence_cannot_replay_the_result_committed_by_a_recovery_fence()
    {
        var appended = FeatureInstallationTransitions.Append(State(), Input("stale-replay"), Now);
        var abandoned = FeatureInstallationTransitions.Claim(
            appended.State,
            "host-abandoned",
            Now,
            TimeSpan.FromSeconds(60));
        var recovered = FeatureInstallationTransitions.Claim(
            abandoned.State,
            "host-recovered",
            Now.AddSeconds(60),
            TimeSpan.FromSeconds(60));
        var recoveredCommit = Commit(Assert.IsType<FeatureRunClaim>(recovered.Claim).Fence);
        var committed = FeatureInstallationTransitions.Commit(
            recovered.State,
            recoveredCommit,
            Now.AddSeconds(61));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureInstallationTransitions.Commit(
            committed.State,
            recoveredCommit with { Fence = Assert.IsType<FeatureRunClaim>(abandoned.Claim).Fence },
            Now.AddSeconds(62)));
    }

    [Fact]
    public void Duplicate_schedule_delivery_coalesces_downtime_to_one_input()
    {
        var occurrence = new FeatureScheduleOccurrence(
            "daily-summary",
            Now.AddHours(-3),
            Now.AddHours(21),
            "{}",
            "correlation-schedule",
            "trace-schedule");

        var first = FeatureInstallationTransitions.RecordScheduleOccurrence(State(), occurrence, Now);
        var duplicate = FeatureInstallationTransitions.RecordScheduleOccurrence(first.State, occurrence, Now.AddMinutes(1));

        Assert.Equal(FeatureAppendStatus.Accepted, first.Status);
        Assert.Equal(FeatureAppendStatus.Duplicate, duplicate.Status);
        Assert.Single(duplicate.State.Inbox);
        Assert.Single(duplicate.State.Schedules);
        Assert.Equal(Now.AddHours(21), duplicate.State.Schedules[0].NextOccurrenceAt);
    }

    [Fact]
    public void Intent_operation_keys_include_installation_input_and_logical_key_and_apply_once()
    {
        var claimed = Claimed();
        var committed = FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(
                claimed.Claim.Fence,
                "{}",
                [new FeatureIntent("notify", FeatureIntentKind.Event, "{}")]),
            Now.AddSeconds(1));
        var intent = Assert.Single(FeatureInstallationTransitions.ListPendingIntents(committed.State));

        Assert.Contains(InstallationId.Value, intent.OperationKey, StringComparison.Ordinal);
        Assert.Contains("input-1", intent.OperationKey, StringComparison.Ordinal);
        Assert.Contains("notify", intent.OperationKey, StringComparison.Ordinal);

        var applied = FeatureInstallationTransitions.ApplyIntent(committed.State, intent.OperationKey, Now.AddSeconds(2));
        var repeated = FeatureInstallationTransitions.ApplyIntent(applied, intent.OperationKey, Now.AddSeconds(3));

        Assert.Empty(FeatureInstallationTransitions.ListPendingIntents(repeated));
        Assert.Equal(applied, repeated);
    }

    [Fact]
    public void Pause_resume_switch_and_rollback_are_explicit_transitions()
    {
        var paused = FeatureInstallationTransitions.Pause(State(), "operator request");
        var resumed = FeatureInstallationTransitions.Resume(paused);
        var switched = FeatureInstallationTransitions.SwitchRelease(resumed, ReleaseTwo);
        var rolledBack = FeatureInstallationTransitions.Rollback(switched);

        Assert.True(paused.Paused);
        Assert.Equal("operator request", paused.PauseReason);
        Assert.False(resumed.Paused);
        Assert.Equal(ReleaseTwo, switched.ActiveRelease);
        Assert.Equal(ReleaseOne, switched.PreviousRelease);
        Assert.Equal(ReleaseOne, rolledBack.ActiveRelease);
        Assert.Null(rolledBack.PreviousRelease);
        Assert.Same(rolledBack, FeatureInstallationTransitions.Rollback(rolledBack));
    }

    [Fact]
    public void Hub_rejects_an_invalid_input_before_it_enters_the_durable_fan_out()
    {
        var hub = FeatureHubTransitions.Register(
            FeatureHubState.Empty,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["email.received"]));
        var invalid = Input("invalid") with { PayloadJson = "not-json" };

        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.BeginFanOut(hub, invalid));
        Assert.Empty(hub.FanOuts);
    }

    [Fact]
    public void Hub_rejects_conflicting_content_for_a_persisted_fan_out_input_id()
    {
        var hub = FeatureHubTransitions.Register(
            FeatureHubState.Empty,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["email.received"]));
        var begun = FeatureHubTransitions.BeginFanOut(hub, Input("fanout-conflict"));
        var conflicting = Input("fanout-conflict") with { PayloadJson = "{\"different\":true}" };

        Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.BeginFanOut(begun, conflicting));
    }

    [Fact]
    public void Hub_rejects_incomplete_or_unknown_capability_constraint_schemas()
    {
        FeatureGrantSpec[] missingToolAllowlist = [new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), "{}", "google")];
        FeatureGrantSpec[] unknownConstraint = [new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), "{\"allowedToolIds\":[\"gmail.message.read.v1\"],\"scope\":\"bounded\"}", "google")];
        FeatureGrantSpec[] mismatchedToolAllowlist = [new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), "{\"allowedToolIds\":[\"gmail.mailbox.read.v1\"]}", "google")];

        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(ReleaseOne, missingToolAllowlist),
            0,
            Now));
        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(ReleaseOne, unknownConstraint),
            0,
            Now));
        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(ReleaseOne, mismatchedToolAllowlist),
            0,
            Now));
    }

    [Fact]
    public void Exact_digest_approval_stages_then_activates_a_complete_grant_set()
    {
        FeatureGrantSpec[] grants =
        [
            new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), Constraints("gmail.message.read.v1"), "google"),
            new("model.complete.v1", 1, null, Constraints("model.complete.v1"))
        ];
        var proposal = Proposal(ReleaseOne, grants);
        var proposed = FeatureHubTransitions.Propose(FeatureHubState.Empty, proposal, 0, Now);
        var approval = Assert.Single(proposed.Approvals);
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseOne, true, "decision-1"),
            proposed.Revision,
            Now.AddSeconds(1));
        var staged = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(
                InstallationId,
                ReleaseOne,
                new ActorId("actor-1"),
                grants),
            approved.Revision);
        var activated = FeatureHubTransitions.Activate(staged, InstallationId, staged.Revision);

        var authority = Assert.Single(activated.Authorities);
        Assert.Equal(ReleaseOne, authority.ActiveRelease);
        Assert.Equal(new GrantRevision(1), authority.ActiveGrantRevision);
        Assert.Null(authority.PendingRelease);
        Assert.Equal(2, authority.ActiveGrants.Length);
        Assert.Equal(
            "gmail.message.read.v1",
            Assert.IsType<FeatureGrantState>(FeatureHubTransitions.ReadGrant(
                activated,
                new FeatureGrantLookup(InstallationId, ReleaseOne, "gmail.message.read.v1", 1))).CapabilityId);
    }

    [Fact]
    public void Confirmed_publication_is_exact_replayable_and_an_authority_change_invalidates_it()
    {
        FeatureGrantSpec[] grants =
        [
            new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), Constraints("gmail.message.read.v1"), "google")
        ];
        var active = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, grants), grants);
        var registered = FeatureHubTransitions.Register(
            active,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["email.received"]));
        var prepared = FeaturePublicationTransitions.Prepare(registered, InstallationId);
        var receipt = new FeaturePublicationReceipt(
            InstallationId,
            prepared.Ticket.PublicationFence,
            prepared.Ticket.AuthorityDigest,
            prepared.Ticket.AccessDigest,
            new string('f', 64));

        var confirmed = FeaturePublicationTransitions.Confirm(prepared.State, receipt);
        var replayed = FeaturePublicationTransitions.Confirm(confirmed.State, receipt);
        var revoked = FeatureHubTransitions.Revoke(
            confirmed.State,
            new FeatureGrantRevocation(InstallationId, ReleaseOne, "gmail.message.read.v1", 1),
            confirmed.State.Revision);

        Assert.Same(confirmed.State, replayed.State);
        Assert.Equal(receipt, replayed.Receipt);
        Assert.True(revoked.Authorities[0].PublicationFence > receipt.PublicationFence);
        Assert.Null(revoked.Authorities[0].PublicationReceipt);
        Assert.Throws<FeatureConcurrencyException>(() => FeaturePublicationTransitions.Confirm(revoked, receipt));
    }

    [Fact]
    public void Publication_authority_digest_includes_previous_release_and_grants_while_access_digest_does_not()
    {
        FeatureGrantSpec[] previousGrants =
        [
            new("capability.previous", 1, null, Constraints("capability.previous"))
        ];
        FeatureGrantSpec[] activeGrants =
        [
            new("capability.active", 1, null, Constraints("capability.active"))
        ];
        var previous = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, previousGrants), previousGrants);
        var active = Activate(previous, Proposal(ReleaseTwo, activeGrants), activeGrants);
        var registered = FeatureHubTransitions.Register(
            active,
            new FeatureInstallationRegistration(InstallationId, ReleaseTwo, ["manual"]));
        var baseline = FeaturePublicationTransitions.Prepare(registered, InstallationId);
        var authorities = baseline.State.Authorities.ToArray();
        authorities[0] = authorities[0] with
        {
            PreviousRelease = new ReleaseDigest(new string('c', 64)),
            PreviousGrants = [new FeatureGrantState("capability.other", 1, null, Constraints("capability.other"), null)]
        };
        var changed = FeaturePublicationTransitions.Prepare(
            baseline.State with { Authorities = authorities },
            InstallationId);

        Assert.NotEqual(baseline.Ticket.AuthorityDigest, changed.Ticket.AuthorityDigest);
        Assert.Equal(baseline.Ticket.AccessDigest, changed.Ticket.AccessDigest);
    }

    [Fact]
    public void Registration_order_is_canonical_and_semantically_identical_replay_preserves_the_publication()
    {
        FeatureGrantSpec[] grants = [new("capability.active", 1, null, Constraints("capability.active"))];
        var active = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, grants), grants);
        var registered = FeatureHubTransitions.Register(
            active,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["z-event", "a-event"]));
        var prepared = FeaturePublicationTransitions.Prepare(registered, InstallationId);
        var receipt = new FeaturePublicationReceipt(
            InstallationId,
            prepared.Ticket.PublicationFence,
            prepared.Ticket.AuthorityDigest,
            prepared.Ticket.AccessDigest,
            new string('e', 64));
        var confirmed = FeaturePublicationTransitions.Confirm(prepared.State, receipt).State;

        var replayed = FeatureHubTransitions.Register(
            confirmed,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["a-event", "z-event"]));
        var changed = FeatureHubTransitions.Register(
            replayed,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["a-event", "other-event"]));

        Assert.Same(confirmed, replayed);
        Assert.Equal(["a-event", "z-event"], replayed.Installations[0].Subscriptions);
        Assert.Equal(receipt, replayed.Authorities[0].PublicationReceipt);
        Assert.True(changed.Authorities[0].PublicationFence > receipt.PublicationFence);
        Assert.Null(changed.Authorities[0].PublicationReceipt);
    }

    [Fact]
    public void Repeated_inbox_full_pause_preserves_an_already_exact_authority_publication_state()
    {
        FeatureGrantSpec[] grants = [new("capability.active", 1, null, Constraints("capability.active"))];
        var active = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, grants), grants);
        var registered = FeatureHubTransitions.Register(
            active,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["email.received"]));
        var prepared = FeaturePublicationTransitions.Prepare(registered, InstallationId);
        var receipt = new FeaturePublicationReceipt(
            InstallationId,
            prepared.Ticket.PublicationFence,
            prepared.Ticket.AuthorityDigest,
            prepared.Ticket.AccessDigest,
            new string('d', 64));
        var confirmed = FeaturePublicationTransitions.Confirm(prepared.State, receipt).State;
        var authorities = confirmed.Authorities.ToArray();
        authorities[0] = authorities[0] with { Paused = true, PauseReason = "feature inbox full" };
        var paused = confirmed with { Authorities = authorities };
        var input = Input("full-replay");
        var begun = FeatureHubTransitions.BeginFanOut(paused, input);

        var first = FeatureHubTransitions.RecordDeliveryOutcomes(
            begun,
            input.InputId,
            [new FeatureDeliveryAttempt(InstallationId, FeatureAppendStatus.Full)],
            Now);
        var replayed = FeatureHubTransitions.RecordDeliveryOutcomes(
            first,
            input.InputId,
            [new FeatureDeliveryAttempt(InstallationId, FeatureAppendStatus.Full)],
            Now.AddSeconds(1));

        Assert.Equal(paused.Authorities[0], first.Authorities[0]);
        Assert.Equal(first.Authorities[0], replayed.Authorities[0]);
        Assert.Equal(receipt, replayed.Authorities[0].PublicationReceipt);
    }

    [Fact]
    public void Every_authority_mutation_advances_the_publication_fence_and_every_exact_no_op_preserves_it()
    {
        FeatureGrantSpec[] firstGrants = [new("capability.first", 1, null, Constraints("capability.first"))];
        FeatureGrantSpec[] secondGrants = [new("capability.second", 1, null, Constraints("capability.second"))];
        var active = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, firstGrants), firstGrants);
        var registered = FeatureHubTransitions.Register(
            active,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["email.received"]));
        var prepared = FeaturePublicationTransitions.Prepare(registered, InstallationId);
        var receipt = new FeaturePublicationReceipt(
            InstallationId,
            prepared.Ticket.PublicationFence,
            prepared.Ticket.AuthorityDigest,
            prepared.Ticket.AccessDigest,
            new string('c', 64));
        var confirmed = FeaturePublicationTransitions.Confirm(prepared.State, receipt).State;
        var confirmedAuthority = confirmed.Authorities[0];

        var paused = FeatureHubTransitions.PauseAuthority(confirmed, InstallationId, "owner pause", confirmed.Revision);
        Assert.Equal(confirmedAuthority.PublicationFence + 1, paused.Authorities[0].PublicationFence);
        Assert.Null(paused.Authorities[0].PublicationReceipt);
        var samePause = FeatureHubTransitions.PauseAuthority(paused, InstallationId, "owner pause", paused.Revision);
        Assert.Same(paused, samePause);
        var changedPause = FeatureHubTransitions.PauseAuthority(paused, InstallationId, "different pause", paused.Revision);
        Assert.Equal(paused.Authorities[0].PublicationFence + 1, changedPause.Authorities[0].PublicationFence);
        var resumed = FeatureHubTransitions.ResumeAuthority(changedPause, InstallationId, changedPause.Revision);
        Assert.Equal(changedPause.Authorities[0].PublicationFence + 1, resumed.Authorities[0].PublicationFence);
        Assert.Same(resumed, FeatureHubTransitions.ResumeAuthority(resumed, InstallationId, resumed.Revision));
        Assert.Same(confirmed, FeatureHubTransitions.RollbackAuthority(confirmed, InstallationId, confirmed.Revision));

        var proposed = FeatureHubTransitions.Propose(confirmed, Proposal(ReleaseTwo, secondGrants), confirmed.Revision, Now);
        var approval = proposed.Approvals.Single(candidate => candidate.Release.Digest == ReleaseTwo);
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseTwo, true, "decision-fence-update"),
            proposed.Revision,
            Now);
        Assert.Equal(receipt, approved.Authorities[0].PublicationReceipt);
        var granted = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(InstallationId, ReleaseTwo, new ActorId("actor-1"), secondGrants),
            approved.Revision);
        Assert.Equal(confirmedAuthority.PublicationFence + 1, granted.Authorities[0].PublicationFence);
        Assert.Null(granted.Authorities[0].PublicationReceipt);
        var activated = FeatureHubTransitions.Activate(granted, InstallationId, granted.Revision);
        Assert.Equal(granted.Authorities[0].PublicationFence + 1, activated.Authorities[0].PublicationFence);
        var registeredUpdate = FeatureHubTransitions.Register(
            activated,
            new FeatureInstallationRegistration(InstallationId, ReleaseTwo, ["email.received"]));
        var updatePrepared = FeaturePublicationTransitions.Prepare(registeredUpdate, InstallationId);
        var updateReceipt = new FeaturePublicationReceipt(
            InstallationId,
            updatePrepared.Ticket.PublicationFence,
            updatePrepared.Ticket.AuthorityDigest,
            updatePrepared.Ticket.AccessDigest,
            new string('b', 64));
        var updateConfirmed = FeaturePublicationTransitions.Confirm(updatePrepared.State, updateReceipt).State;
        var rolledBack = FeatureHubTransitions.RollbackAuthority(updateConfirmed, InstallationId, updateConfirmed.Revision);
        Assert.Equal(updateConfirmed.Authorities[0].PublicationFence + 1, rolledBack.Authorities[0].PublicationFence);
        Assert.Null(rolledBack.Authorities[0].PublicationReceipt);

        var input = Input("first-full-publication");
        var begun = FeatureHubTransitions.BeginFanOut(confirmed, input);
        var full = FeatureHubTransitions.RecordDeliveryOutcomes(
            begun,
            input.InputId,
            [new FeatureDeliveryAttempt(InstallationId, FeatureAppendStatus.Full)],
            Now);
        Assert.Equal(confirmedAuthority.PublicationFence + 1, full.Authorities[0].PublicationFence);
        Assert.Null(full.Authorities[0].PublicationReceipt);
        Assert.True(full.Authorities[0].Paused);

        var legacyAuthorities = registered.Authorities.ToArray();
        legacyAuthorities[0] = legacyAuthorities[0] with { PublicationFence = 0, PublicationReceipt = null };
        var legacy = registered with { Authorities = legacyAuthorities };
        var upgraded = FeaturePublicationTransitions.Prepare(legacy, InstallationId);
        Assert.Equal(1, upgraded.Ticket.PublicationFence);
        Assert.Equal(1, upgraded.State.Authorities[0].PublicationFence);
        Assert.Equal(legacy.Revision + 1, upgraded.State.Revision);
    }

    [Fact]
    public void Approval_cannot_be_replayed_for_another_digest_or_an_incomplete_grant_set()
    {
        FeatureGrantSpec[] grants =
        [
            new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), Constraints("gmail.message.read.v1"), "google"),
            new("model.complete.v1", 1, null, Constraints("model.complete.v1"))
        ];
        var proposed = FeatureHubTransitions.Propose(
            FeatureHubState.Empty,
            Proposal(ReleaseOne, grants),
            0,
            Now);
        var approval = Assert.Single(proposed.Approvals);

        var wrongDigest = Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseTwo, true, "decision-wrong-digest"),
            proposed.Revision,
            Now.AddSeconds(1)));

        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseOne, true, "decision-1"),
            proposed.Revision,
            Now.AddSeconds(1));
        var alreadyDecided = Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.Decide(
            approved,
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseOne, true, "decision-repeated"),
            approved.Revision,
            Now.AddSeconds(2)));
        var missingApproval = Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.Grant(
            FeatureHubState.Empty,
            new FeatureGrantRequest(
                InstallationId,
                ReleaseOne,
                new ActorId("actor-1"),
                grants),
            0));
        var incompleteGrantSet = Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(
                InstallationId,
                ReleaseOne,
                new ActorId("actor-1"),
                [new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), Constraints("gmail.message.read.v1"), "google")]),
            approved.Revision));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, missingApproval.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, incompleteGrantSet.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, wrongDigest.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, alreadyDecided.Reason);
    }

    [Fact]
    public void Activation_without_an_exact_staged_grant_set_is_a_precondition_failure()
    {
        FeatureGrantSpec[] grants = [new("capability.active", 1, null, Constraints("capability.active"))];
        var active = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, grants), grants);

        var rejected = Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureHubTransitions.Activate(active, InstallationId, active.Revision));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
    }

    [Fact]
    public void Confirmed_publication_reservation_failures_distinguish_actor_authority_from_expected_state()
    {
        FeatureGrantSpec[] grants = [new("capability.active", 1, null, Constraints("capability.active"))];
        var active = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, grants), grants);
        var registered = FeatureHubTransitions.Register(
            active,
            new FeatureInstallationRegistration(InstallationId, ReleaseOne, ["manual"]));
        var prepared = FeaturePublicationTransitions.Prepare(registered, InstallationId);
        var receipt = new FeaturePublicationReceipt(
            InstallationId,
            prepared.Ticket.PublicationFence,
            prepared.Ticket.AuthorityDigest,
            prepared.Ticket.AccessDigest,
            new string('f', 64));
        var confirmed = FeaturePublicationTransitions.Confirm(prepared.State, receipt).State;
        var approval = confirmed.Approvals.Single(candidate => candidate.Release.Digest == ReleaseOne);
        var reservation = new FeatureDraftInstallationReservation(
            new FeatureDraftId("draft-publication-reasons"),
            1,
            InstallationId,
            ReleaseOne,
            "install-publication-reasons",
            new string('c', 64),
            prepared.Ticket.AccessDigest,
            approval.DecisionId!,
            new ActorId("actor-1"));

        var missingPublication = Assert.Throws<FeatureConcurrencyException>(() =>
            FeaturePublicationTransitions.DemandConfirmedReservation(registered, reservation));
        var mismatchedAccess = Assert.Throws<FeatureConcurrencyException>(() =>
            FeaturePublicationTransitions.DemandConfirmedReservation(
                confirmed,
                reservation with { AccessDigest = new string('0', 64) }));
        var mismatchedDecision = Assert.Throws<FeatureConcurrencyException>(() =>
            FeaturePublicationTransitions.DemandConfirmedReservation(
                confirmed,
                reservation with { DecisionId = "decision-other" }));
        var actorMismatch = Assert.Throws<FeatureAuthorityRejectedException>(() =>
            FeaturePublicationTransitions.DemandConfirmedReservation(
                confirmed,
                reservation with { ActorId = new ActorId("actor-other") }));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, missingPublication.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, mismatchedAccess.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, mismatchedDecision.Reason);
        Assert.Equal(FeatureAuthorityRejectionReason.ActorMismatch, actorMismatch.Reason);
    }

    [Fact]
    public void Update_keeps_previous_grants_for_drain_and_pause_or_revoke_denies_the_next_operation()
    {
        FeatureGrantSpec[] readGrant = [new("gmail.message.read.v1", 1, new ProviderConnectionId("google-1"), Constraints("gmail.message.read.v1"), "google")];
        FeatureGrantSpec[] modelGrant = [new("model.complete.v1", 1, null, Constraints("model.complete.v1"))];
        var active = Activate(FeatureHubState.Empty, Proposal(ReleaseOne, readGrant), readGrant);
        var updated = Activate(active, Proposal(ReleaseTwo, modelGrant), modelGrant);

        Assert.NotNull(FeatureHubTransitions.ReadGrant(
            updated,
            new FeatureGrantLookup(InstallationId, ReleaseOne, "gmail.message.read.v1", 1)));
        Assert.NotNull(FeatureHubTransitions.ReadGrant(
            updated,
            new FeatureGrantLookup(InstallationId, ReleaseTwo, "model.complete.v1", 1)));

        var paused = FeatureHubTransitions.PauseAuthority(
            updated,
            InstallationId,
            "owner pause",
            updated.Revision);
        Assert.Null(FeatureHubTransitions.ReadGrant(
            paused,
            new FeatureGrantLookup(InstallationId, ReleaseTwo, "model.complete.v1", 1)));

        var resumed = FeatureHubTransitions.ResumeAuthority(paused, InstallationId, paused.Revision);
        var revoked = FeatureHubTransitions.Revoke(
            resumed,
            new FeatureGrantRevocation(InstallationId, ReleaseTwo, "model.complete.v1", 1),
            resumed.Revision);
        Assert.Null(FeatureHubTransitions.ReadGrant(
            revoked,
            new FeatureGrantLookup(InstallationId, ReleaseTwo, "model.complete.v1", 1)));
    }

    private static FeatureHubState Activate(
        FeatureHubState state,
        FeatureReleaseProposal proposal,
        FeatureGrantSpec[] grants)
    {
        var proposed = FeatureHubTransitions.Propose(state, proposal, state.Revision, Now);
        var approval = proposed.Approvals[^1];
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(approval.ApprovalId, proposal.Release.Digest, true, "decision-" + proposed.Revision),
            proposed.Revision,
            Now);
        var staged = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(InstallationId, proposal.Release.Digest, new ActorId("actor-1"), grants),
            approved.Revision);
        return FeatureHubTransitions.Activate(staged, InstallationId, staged.Revision);
    }

    private static FeatureReleaseProposal Proposal(ReleaseDigest release, FeatureGrantSpec[] grants) => new(
        InstallationId,
        new FeatureReleaseMetadata(
            release,
            "sha256:" + release.Value,
            FeatureSourceKind.Repository,
            grants.Select(grant => grant.CapabilityId).ToArray(),
            ["DigitalBrain.Features.Sdk"]),
        grants);

    private static string Constraints(string capabilityId) =>
        JsonSerializer.Serialize(new { allowedToolIds = new[] { capabilityId } });

    private static FeatureInstallationState State() =>
        FeatureInstallationState.Create(ReleaseOne, InstallationId);

    private static (FeatureInstallationState State, FeatureRunClaim Claim) Claimed()
    {
        var appended = FeatureInstallationTransitions.Append(State(), Input("input-1"), Now);
        var claimed = FeatureInstallationTransitions.Claim(
            appended.State,
            "host-1",
            Now,
            TimeSpan.FromSeconds(60));
        return (claimed.State, Assert.IsType<FeatureRunClaim>(claimed.Claim));
    }

    private static FeatureInput Input(string inputId) => new(
        inputId,
        "email.received",
        "{}",
        Now,
        $"correlation-{inputId}",
        $"trace-{inputId}");

    private static FeatureRunCommit Commit(
        FeatureLeaseFence fence,
        string stateJson = "{}",
        IReadOnlyList<FeatureIntent>? intents = null) => new(
            fence,
            stateJson,
            intents ?? [],
            new FeatureResourceUsage(0, 0),
            "{\"ok\":true}");
}
