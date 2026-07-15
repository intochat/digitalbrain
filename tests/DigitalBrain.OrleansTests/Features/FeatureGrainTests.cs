using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Concurrency;
using Orleans.Runtime;
using Orleans.Serialization;

namespace DigitalBrain.OrleansTests.Features;

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class FeatureGrainTests(FeatureGrainClusterFixture fixture)
{
    private static readonly BrainOwnerId Owner = new("owner-1");
    private static readonly ReleaseDigest ReleaseOne = new(new string('a', 64));
    private static readonly ReleaseDigest ReleaseTwo = new(new string('b', 64));

    [Fact]
    public async Task Feature_Draft_authoring_operations_persist_replay_and_remain_owner_local()
    {
        var owner = new BrainOwnerId("owner-draft-authoring-grain");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var otherHub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(new BrainOwnerId("owner-draft-authoring-other")));
        var createdAt = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-draft-authoring",
            "Create an owner-local Feature",
            createdAt,
            "conversation-draft-authoring"));
        var behavior = new FeatureBehavior([
            new FeatureScenario(
                "scenario-grain",
                "Create an outcome",
                "the owner has a request",
                "the Feature runs",
                "the outcome is available")
        ]);
        var reviseBehavior = new ReviseFeatureBehavior(
            draft.DraftId,
            behavior,
            0,
            "behavior-grain",
            createdAt.AddMinutes(1));

        var behaviorResult = await hub.ReviseBehaviorAsync(reviseBehavior);
        var sourceResult = await hub.ReviseSourceAsync(new ReviseFeatureSource(
            draft.DraftId,
            GrainSource(),
            1,
            "source-grain",
            createdAt.AddMinutes(2)));
        var verification = FeatureVerificationTestData.Passing(ReleaseOne, sourceResult.Source, 1, createdAt.AddMinutes(3));
        var verificationResult = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            verification,
            2,
            "verification-grain"));

        Assert.Equal(draft.DraftId, (await hub.ReadDraftAsync(draft.DraftId))?.DraftId);
        Assert.Null(await otherHub.ReadDraftAsync(draft.DraftId));
        Assert.Equal(1, behaviorResult.Revision);
        Assert.Equal(2, sourceResult.Revision);
        Assert.Equal(3, verificationResult.Revision);

        await fixture.Cluster.DeactivateAsync((IAddressable)hub);

        var replay = await hub.ReviseBehaviorAsync(reviseBehavior);
        Assert.Equal(1, replay.Revision);
        Assert.Equal(3, (await hub.ReadDraftAsync(draft.DraftId))?.Revision);

        var installationId = new FeatureInstallationId("installation-draft-authoring");
        await hub.AcquireDraftInstallationReservationAsync(new InstallFeatureVersion(
            draft.DraftId,
            3,
            installationId,
            ReleaseOne,
            [],
            ["manual"],
            "decision-installed-grain",
            "installed-grain"), new ActorId("actor-draft-authoring"));
        var snapshot = await hub.ReadAsync();
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(
                    ReleaseOne,
                    "sha256:" + ReleaseOne.Value,
                    FeatureSourceKind.RuntimeAuthored,
                    [],
                    []),
                []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.DecideAsync(
            new FeatureApprovalDecision(
                approval.ApprovalId,
                ReleaseOne,
                true,
                "decision-installed-grain",
                new ActorId("actor-draft-authoring")),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseOne, new ActorId("actor-draft-authoring"), []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, ["manual"]),
            snapshot.Revision);
        await fixture.PublishActiveAsync(owner, hub, installationId);
        var installed = await hub.MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
            draft.DraftId,
            installationId,
            ReleaseOne,
            3,
            "installed-grain",
            createdAt.AddMinutes(4)));
        Assert.Equal("installed", installed.Status);
        await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => hub.ReviseSourceAsync(new ReviseFeatureSource(
            draft.DraftId,
            GrainSource(),
            4,
            "source-after-install",
            createdAt.AddMinutes(5))));
    }

    [Fact]
    public async Task Direct_grain_verification_rejects_unbounded_evidence_without_persisting_it()
    {
        var owner = new BrainOwnerId("owner-invalid-verification-evidence");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var now = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-invalid-verification-evidence",
            "Reject invalid verification evidence",
            now,
            "conversation-invalid-verification-evidence"));
        var evidence = new FeatureVerificationEvidence(
            $"sha256:{new string('b', 64)}",
            1,
            0,
            1,
            0,
            [new FeatureScenarioEvidence("scenario-invalid", "Invalid evidence", FeatureScenarioOutcome.Failed, new string('x', 4097), 1)],
            []);

        await Assert.ThrowsAsync<ArgumentException>(() => hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            new FeatureVerification(ReleaseOne, 1, 0, 1, 0, now, evidence),
            draft.Revision,
            "verification-invalid-evidence")));
        var persisted = Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId));
        Assert.Null(persisted.Verification);
        Assert.Equal(draft.Revision, persisted.Revision);
    }

    [Fact]
    public async Task A_forged_deterministic_publication_receipt_cannot_confirm_or_finalize_a_Draft()
    {
        var owner = new BrainOwnerId("owner-forged-publication");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var installationId = new FeatureInstallationId("installation-forged-publication");
        var now = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-forged-publication",
            "Reject a forged publication receipt",
            now,
            "conversation-forged-publication"));
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(ReleaseOne, draft.Source, 1, now),
            draft.Revision,
            "verify-forged-publication"));
        await hub.AcquireDraftInstallationReservationAsync(new InstallFeatureVersion(
            draft.DraftId,
            draft.Revision,
            installationId,
            ReleaseOne,
            [],
            ["manual"],
            "decision-forged-publication",
            "install-forged-publication"), new ActorId("actor-reservation"));
        var snapshot = await hub.ReadAsync();
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(ReleaseOne, "sha256:" + ReleaseOne.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.DecideAsync(
            new FeatureApprovalDecision(
                approval.ApprovalId,
                ReleaseOne,
                true,
                "decision-forged-publication",
                new ActorId("actor-reservation")),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseOne, new ActorId("actor-reservation"), []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, ["manual"]),
            snapshot.Revision);
        var ticket = await hub.PrepareActivePublicationAsync(installationId);
        var forged = new FeaturePublicationReceipt(
            installationId,
            ticket.PublicationFence,
            ticket.AuthorityDigest,
            ticket.AccessDigest,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                FeaturePublicationManifestCodec.Serialize(owner, ticket))));

        var nullReceipt = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            hub.ConfirmActivePublicationAsync(null!));
        var forgedReceipt = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            hub.ConfirmActivePublicationAsync(forged));
        fixture.PublicationVerifier.Allow(owner, ticket, forged);
        fixture.PublicationVerifier.Fail(owner, new FeatureConcurrencyException(
            "publication-precondition-canary",
            FeatureCommandRejectionReason.Precondition));
        var verifierPrecondition = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            hub.ConfirmActivePublicationAsync(forged));
        fixture.PublicationVerifier.Fail(owner, new FeatureConcurrencyException(
            "publication-conflict-canary",
            FeatureCommandRejectionReason.Conflict));
        var verifierConflict = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            hub.ConfirmActivePublicationAsync(forged));
        fixture.PublicationVerifier.Fail(owner, new ArgumentException("publication-argument-canary"));
        var verifierArgument = await Assert.ThrowsAsync<ArgumentException>(() =>
            hub.ConfirmActivePublicationAsync(forged));
        fixture.PublicationVerifier.Fail(owner, new InvalidOperationException("publication-invalid-operation-canary"));
        var verifierInvalidOperation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            hub.ConfirmActivePublicationAsync(forged));
        fixture.PublicationVerifier.Fail(owner, new IOException("publication-verifier-canary"));
        var verifierUnavailable = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            hub.ConfirmActivePublicationAsync(forged));
        Assert.Equal(forged, await hub.ConfirmActivePublicationAsync(forged));
        var installed = await hub.MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
            draft.DraftId,
            installationId,
            ReleaseOne,
            draft.Revision,
            "install-forged-publication",
            now.AddMinutes(1)));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, nullReceipt.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, forgedReceipt.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, verifierPrecondition.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Conflict, verifierConflict.Reason);
        Assert.DoesNotContain("FeatureCommandRejectedException", verifierArgument.GetType().Name, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureCommandRejectedException", verifierInvalidOperation.GetType().Name, StringComparison.Ordinal);
        Assert.Equal(FeatureCommandRejectionReason.Unavailable, verifierUnavailable.Reason);
        Assert.Equal("installed", installed.Status);
        Assert.Null(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));
    }

    [Fact]
    public async Task Feature_Draft_authoring_reconciles_a_deep_cloned_durable_write_after_an_acknowledgement_failure()
    {
        var owner = new BrainOwnerId("owner-draft-authoring-reconciliation");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var createdAt = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-draft-reconciliation",
            "Create a reconciled Feature",
            createdAt,
            "conversation-draft-reconciliation"));
        fixture.Storage.CommitCompetingStateThenFailNextWrite(state => DeepClone((FeatureHubState)state));

        var revised = await hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
            draft.DraftId,
            new FeatureBehavior([
                new FeatureScenario("scenario-reconciled", "Reconcile", "a write commits", "the acknowledgement fails", "the durable state is retained")
            ]),
            0,
            "behavior-reconciled",
            createdAt.AddMinutes(1)));

        Assert.Equal(1, revised.Revision);
        Assert.Equal(1, (await hub.ReadDraftAsync(draft.DraftId))?.Revision);
    }

    [Fact]
    public async Task Resetting_a_new_installation_discards_only_a_pristine_unpublished_runtime_and_replays()
    {
        var prepared = await PreparePendingDraftInstallationAsync("reset-pristine");
        var installation = fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(prepared.Owner, prepared.Command.InstallationId));
        var reservation = Assert.IsType<FeatureDraftInstallationReservation>(
            await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        await installation.ActivateReservedReleaseAsync(RuntimeReservation(prepared.Owner, reservation));
        var reset = new ResetFeatureDraftInstallationReservation(
            prepared.Command.DraftId,
            "reset-pristine",
            prepared.Command);

        var result = await prepared.Hub.ResetDraftInstallationReservationAsync(reset, prepared.ActorId);
        var replay = await prepared.Hub.ResetDraftInstallationReservationAsync(
            reset with { ReservedInstallation = null },
            prepared.ActorId);

        Assert.True(result.Completed);
        Assert.True(replay.Completed);
        Assert.Equal(result.Draft.DraftId, replay.Draft.DraftId);
        Assert.Equal(result.Draft.Revision, replay.Draft.Revision);
        Assert.Null(result.Draft.Verification);
        Assert.Null(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        Assert.Equal(FeatureApprovalStatus.Superseded, (await prepared.Hub.ReadAsync()).Approvals.Single().Status);
        var absent = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.ReadAsync());
        Assert.Equal(FeatureCommandRejectionReason.Precondition, absent.Reason);
    }

    [Fact]
    public async Task New_install_reset_retries_after_the_final_hub_write_fails_with_the_runtime_hold_released()
    {
        var prepared = await PreparePendingDraftInstallationAsync("reset-new-final-write");
        var installation = fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(prepared.Owner, prepared.Command.InstallationId));
        var reset = new ResetFeatureDraftInstallationReservation(
            prepared.Command.DraftId,
            "reset-new-final-write",
            prepared.Command);
        fixture.Storage.FailNextWriteForState("feature-hub");

        await Assert.ThrowsAsync<OrleansException>(() =>
            prepared.Hub.ResetDraftInstallationReservationAsync(reset, prepared.ActorId));

        Assert.NotNull(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        Assert.Null(await installation.ReadReservationAsync());

        var completed = await prepared.Hub.ResetDraftInstallationReservationAsync(reset, prepared.ActorId);

        Assert.True(completed.Completed);
        Assert.Null(completed.Draft.Verification);
        Assert.Null(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        Assert.Null(await installation.ReadReservationAsync());
        var absent = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.ReadAsync());
        Assert.Equal(FeatureCommandRejectionReason.Precondition, absent.Reason);
    }

    [Fact]
    public async Task Resetting_an_update_preserves_or_exactly_restores_the_runtime_and_rejects_candidate_era_mutation()
    {
        var owner = new BrainOwnerId("owner-reset-update-grain");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var installationId = new FeatureInstallationId("installation-reset-update-grain");
        var actor = new ActorId("actor-reset-update-grain");
        var activeApproval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(ReleaseOne, "sha256:" + ReleaseOne.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            0);
        await hub.DecideAsync(
            new FeatureApprovalDecision(activeApproval.ApprovalId, ReleaseOne, true, "decision-active-update-grain", actor),
            (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseOne, actor, []),
            (await hub.ReadAsync()).Revision);
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, ["active-event"]),
            (await hub.ReadAsync()).Revision);
        await fixture.PublishActiveAsync(owner, hub, installationId);
        var installation = fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(owner, installationId));
        var runtimeBefore = await installation.ReadAsync();
        var now = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-reset-update-grain",
            "Reset a Feature update",
            now,
            "conversation-reset-update-grain"));
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(ReleaseTwo, draft.Source, 1, now),
            draft.Revision,
            "verification-reset-update-grain"));
        var command = new InstallFeatureVersion(
            draft.DraftId,
            draft.Revision,
            installationId,
            ReleaseTwo,
            [],
            ["candidate-event"],
            "decision-reset-update-grain",
            "install-reset-update-grain",
            runtimeBefore.Revision,
            runtimeBefore.ActiveRelease,
            runtimeBefore.PreviousRelease);
        await hub.AcquireDraftInstallationReservationAsync(command, actor);
        var candidateApproval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(ReleaseTwo, "sha256:" + ReleaseTwo.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            (await hub.ReadAsync()).Revision);
        await hub.DecideAsync(
            new FeatureApprovalDecision(candidateApproval.ApprovalId, ReleaseTwo, true, command.DecisionId, actor),
            (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseTwo, actor, []),
            (await hub.ReadAsync()).Revision);

        var preparedReset = await hub.ResetDraftInstallationReservationAsync(
            new ResetFeatureDraftInstallationReservation(command.DraftId, "reset-update-grain", command),
            actor);
        Assert.False(preparedReset.Completed);
        Assert.True(preparedReset.RequiresRepublish);
        Assert.NotNull(await hub.ReadDraftInstallationReservationAsync(command.DraftId));
        Assert.NotNull((await hub.ReadDraftAsync(command.DraftId))?.Verification);
        await fixture.PublishActiveAsync(owner, hub, installationId);
        var completedReset = await hub.CompleteDraftInstallationReservationResetAsync(
            command.DraftId,
            "reset-update-grain",
            actor);
        var runtimeAfter = await installation.ReadAsync();
        var resetSnapshot = await hub.ReadAsync();
        var authority = Assert.Single(resetSnapshot.Authorities);

        Assert.Equal(runtimeBefore.ActiveRelease, runtimeAfter.ActiveRelease);
        Assert.Equal(runtimeBefore.PreviousRelease, runtimeAfter.PreviousRelease);
        Assert.Equal(runtimeBefore.StateJson, runtimeAfter.StateJson);
        Assert.Equal(runtimeBefore.Revision, runtimeAfter.Revision);
        Assert.Null(completedReset.Verification);
        Assert.Equal(ReleaseOne, authority.ActiveRelease);
        Assert.Null(authority.PendingRelease);
        Assert.Equal(ReleaseOne, Assert.Single(resetSnapshot.Installations).Release);
        Assert.Equal(ReleaseOne, Assert.Single(resetSnapshot.Releases).Digest);

        var orphan = await PreparePendingUpdateAsync("reset-update-orphan");
        var orphanBefore = await orphan.Installation.ReadAsync();
        var orphanReservation = Assert.IsType<FeatureDraftInstallationReservation>(
            await orphan.Hub.ReadDraftInstallationReservationAsync(orphan.Command.DraftId));
        await orphan.Installation.ActivateReservedReleaseAsync(
            RuntimeReservation(orphan.Owner, orphanReservation));
        var reconciled = await orphan.Hub.ResetDraftInstallationReservationAsync(
            new ResetFeatureDraftInstallationReservation(orphan.Command.DraftId, "reset-update-orphan", orphan.Command),
            orphan.ActorId);
        var orphanAfter = await orphan.Installation.ReadAsync();

        Assert.True(reconciled.RequiresRepublish);
        Assert.NotNull(await orphan.Hub.ReadDraftInstallationReservationAsync(orphan.Command.DraftId));
        Assert.Equal(orphanBefore.ActiveRelease, orphanAfter.ActiveRelease);
        Assert.Equal(orphanBefore.PreviousRelease, orphanAfter.PreviousRelease);
        Assert.Equal(orphanBefore.Revision + 2, orphanAfter.Revision);

        var mutated = await PreparePendingUpdateAsync("reset-update-mutated");
        var mutatedReservation = Assert.IsType<FeatureDraftInstallationReservation>(
            await mutated.Hub.ReadDraftInstallationReservationAsync(mutated.Command.DraftId));
        await mutated.Installation.ActivateReservedReleaseAsync(
            RuntimeReservation(mutated.Owner, mutatedReservation));
        var blockedPause = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            mutated.Installation.PauseAsync("candidate mutation"));
        Assert.Equal(FeatureCommandRejectionReason.Conflict, blockedPause.Reason);
        Assert.Equal(FeatureAppendStatus.Paused, await mutated.Installation.AppendAsync(Input("candidate-input")));
        var resetMutation = await mutated.Hub.ResetDraftInstallationReservationAsync(
            new ResetFeatureDraftInstallationReservation(mutated.Command.DraftId, "reset-update-mutated", mutated.Command),
            mutated.ActorId);

        Assert.True(resetMutation.RequiresRepublish);
        Assert.NotNull(await mutated.Hub.ReadDraftInstallationReservationAsync(mutated.Command.DraftId));
        Assert.Equal(ReleaseOne, (await mutated.Installation.ReadAsync()).ActiveRelease);
    }

    [Fact]
    public async Task Reset_restores_a_failed_hub_switch_without_losing_active_work_after_reservation()
    {
        var prepared = await PreparePendingUpdateAsync("reset-switch-race");
        var baseline = await prepared.Installation.ReadAsync();
        var hubBeforeSwitch = await prepared.Hub.ReadAsync();
        var reservation = Assert.IsType<FeatureDraftInstallationReservation>(
            await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        await prepared.Installation.ActivateReservedReleaseAsync(
            RuntimeReservation(prepared.Owner, reservation));
        fixture.Storage.FailNextWrite();

        await Assert.ThrowsAsync<OrleansException>(() => prepared.Hub.InstallAsync(
            new FeatureInstallationRegistration(
                prepared.Command.InstallationId,
                prepared.Command.Release,
                prepared.Command.Subscriptions),
            hubBeforeSwitch.Revision));

        var orphan = await prepared.Installation.ReadAsync();
        Assert.Equal(prepared.Command.Release, orphan.ActiveRelease);
        Assert.Equal(baseline.ActiveRelease, orphan.PreviousRelease);
        Assert.Empty(orphan.Inbox);
        Assert.Equal(FeatureRuntimeReservationPhase.Switched,
            (await prepared.Installation.ReadReservationAsync())?.Phase);

        var reset = await prepared.Hub.ResetDraftInstallationReservationAsync(
            new ResetFeatureDraftInstallationReservation(
                prepared.Command.DraftId,
                "reset-switch-race",
                prepared.Command),
            prepared.ActorId);
        var restored = await prepared.Installation.ReadAsync();

        Assert.True(reset.RequiresRepublish);
        Assert.Equal(baseline.ActiveRelease, restored.ActiveRelease);
        Assert.Equal(baseline.PreviousRelease, restored.PreviousRelease);
        Assert.Empty(restored.Inbox);
        Assert.Equal(baseline.Revision + 2, restored.Revision);
        Assert.Equal(FeatureRuntimeReservationPhase.Resetting,
            (await prepared.Installation.ReadReservationAsync())?.Phase);
    }

    [Fact]
    public async Task Confirmed_same_release_candidate_refuses_reset_and_completes_only_through_exact_forward_recovery()
    {
        var prepared = await PreparePendingUpdateAsync("confirmed-same-release", ReleaseOne);
        var snapshot = await prepared.Hub.ReadAsync();
        await prepared.Hub.InstallAsync(
            new FeatureInstallationRegistration(
                prepared.Command.InstallationId,
                prepared.Command.Release,
                prepared.Command.Subscriptions),
            snapshot.Revision);
        await fixture.PublishActiveAsync(prepared.Owner, prepared.Hub, prepared.Command.InstallationId);

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            prepared.Hub.ResetDraftInstallationReservationAsync(
                new ResetFeatureDraftInstallationReservation(
                    prepared.Command.DraftId,
                    "reset-confirmed-same-release",
                    prepared.Command),
                prepared.ActorId));
        var installed = await prepared.Hub.MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
            prepared.Command.DraftId,
            prepared.Command.InstallationId,
            prepared.Command.Release,
            prepared.Command.ExpectedRevision,
            prepared.Command.IdempotencyId,
            fixture.Time.GetUtcNow()));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        Assert.Equal("installed", installed.Status);
        Assert.Null(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
    }

    [Fact]
    public async Task Activated_new_install_refuses_runtime_discard_and_completes_only_through_exact_forward_recovery()
    {
        var prepared = await PreparePendingDraftInstallationAsync("activated-new-forward");
        var snapshot = await prepared.Hub.ReadAsync();
        await prepared.Hub.InstallAsync(
            new FeatureInstallationRegistration(
                prepared.Command.InstallationId,
                prepared.Command.Release,
                prepared.Command.Subscriptions),
            snapshot.Revision);

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            prepared.Hub.ResetDraftInstallationReservationAsync(
                new ResetFeatureDraftInstallationReservation(
                    prepared.Command.DraftId,
                    "reset-activated-new-forward",
                    prepared.Command),
                prepared.ActorId));
        await fixture.PublishActiveAsync(prepared.Owner, prepared.Hub, prepared.Command.InstallationId);
        var installed = await prepared.Hub.MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
            prepared.Command.DraftId,
            prepared.Command.InstallationId,
            prepared.Command.Release,
            prepared.Command.ExpectedRevision,
            prepared.Command.IdempotencyId,
            fixture.Time.GetUtcNow()));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        Assert.Equal("installed", installed.Status);
        Assert.Equal(prepared.Command.InstallationId, installed.InstallationId);
        Assert.Null(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
    }

    [Fact]
    public async Task Publication_preflight_rejects_a_markerless_direct_runtime_switch()
    {
        var prepared = await PreparePendingUpdateAsync("markerless-publication");
        var before = await prepared.Installation.ReadAsync();
        var blocked = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            prepared.Installation.SwitchReleaseAsync(prepared.Command.Release));

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            prepared.Hub.PrepareActivePublicationAsync(prepared.Command.InstallationId));

        Assert.Equal(FeatureCommandRejectionReason.Conflict, blocked.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        Assert.Equal(before, await prepared.Installation.ReadAsync());
        Assert.Equal(FeatureRuntimeReservationPhase.Reserved,
            (await prepared.Installation.ReadReservationAsync())?.Phase);
    }

    [Fact]
    public async Task Publication_marker_binds_previous_release_reserved_revision_and_one_step_switch_structure()
    {
        var prepared = await PreparePendingUpdateAsync("publication-marker-contract");
        var reservation = Assert.IsType<FeatureDraftInstallationReservation>(
            await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        var baseline = Assert.IsType<FeatureInstallationAuthorityBaseline>(reservation.AuthorityBaseline);
        var reservedRevision = Assert.IsType<long>(reservation.RuntimeRevision);
        var marker = new FeatureReleaseSwitchSnapshot(
            reservation.CommandDigest,
            baseline.ActiveRelease,
            baseline.PreviousRelease,
            reservation.Release,
            reservedRevision + 3,
            reservedRevision + 4);
        var runtime = (await prepared.Installation.ReadAsync()) with
        {
            ActiveRelease = reservation.Release,
            PreviousRelease = baseline.ActiveRelease,
            Revision = reservedRevision + 5,
            UnconfirmedReleaseSwitch = marker
        };

        FeatureHubGrain.DemandExactReservedReleaseSwitch(runtime, reservation, baseline, reservation.Release);

        Assert.Equal(
            FeatureCommandRejectionReason.Precondition,
            Assert.Throws<FeatureCommandRejectedException>(() =>
                FeatureHubGrain.DemandExactReservedReleaseSwitch(
                    runtime with
                    {
                        UnconfirmedReleaseSwitch = marker with
                        {
                            FromPreviousRelease = baseline.PreviousRelease is null ? ReleaseTwo : null
                        }
                    },
                    reservation,
                    baseline,
                    reservation.Release)).Reason);
        Assert.Equal(
            FeatureCommandRejectionReason.Precondition,
            Assert.Throws<FeatureCommandRejectedException>(() =>
                FeatureHubGrain.DemandExactReservedReleaseSwitch(
                    runtime with
                    {
                        UnconfirmedReleaseSwitch = marker with { FromRevision = reservedRevision - 1 }
                    },
                    reservation,
                    baseline,
                    reservation.Release)).Reason);
        Assert.Equal(
            FeatureCommandRejectionReason.Precondition,
            Assert.Throws<FeatureCommandRejectedException>(() =>
                FeatureHubGrain.DemandExactReservedReleaseSwitch(
                    runtime with
                    {
                        UnconfirmedReleaseSwitch = marker with { SwitchRevision = marker.FromRevision + 2 }
                    },
                    reservation,
                    baseline,
                    reservation.Release)).Reason);
        Assert.Equal(
            FeatureCommandRejectionReason.Precondition,
            Assert.Throws<FeatureCommandRejectedException>(() =>
                FeatureHubGrain.DemandExactReservedReleaseSwitch(
                    runtime with { Revision = marker.SwitchRevision - 1 },
                    reservation,
                    baseline,
                    reservation.Release)).Reason);
    }

    [Fact]
    public async Task Reservation_blocks_competing_registration_authority_and_rollback_mutations()
    {
        var prepared = await PreparePendingUpdateAsync("reservation-mutation-guard");
        var before = await prepared.Hub.ReadAsync();
        Func<Task>[] mutations =
        [
            async () => await prepared.Hub.RegisterAsync(new FeatureInstallationRegistration(
                prepared.Command.InstallationId,
                ReleaseOne,
                ["active-event"])),
            async () => await prepared.Hub.PauseInstallationAsync(
                prepared.Command.InstallationId,
                "competing pause",
                before.Revision),
            async () => await prepared.Hub.ResumeInstallationAsync(
                prepared.Command.InstallationId,
                before.Revision),
            async () => await prepared.Hub.RevokeAsync(
                new FeatureGrantRevocation(
                    prepared.Command.InstallationId,
                    ReleaseOne,
                    "capability.absent",
                    1),
                before.Revision),
            async () => await prepared.Hub.RollbackInstallationExactAsync(new RollbackFeatureInstallation(
                prepared.Command.InstallationId,
                ReleaseOne,
                ReleaseTwo,
                before.Revision,
                "rollback-reservation-mutation-guard"))
        ];

        foreach (var mutation in mutations)
        {
            var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(mutation);
            Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        }

        var after = await prepared.Hub.ReadAsync();
        Assert.Equal(before.Revision, after.Revision);
        var beforeRegistration = Assert.Single(before.Installations);
        var afterRegistration = Assert.Single(after.Installations);
        Assert.Equal(beforeRegistration.InstallationId, afterRegistration.InstallationId);
        Assert.Equal(beforeRegistration.Release, afterRegistration.Release);
        Assert.Equal(beforeRegistration.Subscriptions, afterRegistration.Subscriptions);
        var beforeAuthority = Assert.Single(before.Authorities);
        var afterAuthority = Assert.Single(after.Authorities);
        Assert.Equal(beforeAuthority.ActiveRelease, afterAuthority.ActiveRelease);
        Assert.Equal(beforeAuthority.ActiveGrantRevision, afterAuthority.ActiveGrantRevision);
        Assert.Equal(beforeAuthority.PendingRelease, afterAuthority.PendingRelease);
        Assert.Equal(beforeAuthority.PendingGrantRevision, afterAuthority.PendingGrantRevision);
        Assert.Equal(beforeAuthority.Paused, afterAuthority.Paused);
        Assert.NotNull(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
    }

    [Fact]
    public async Task Pre_marker_full_backpressure_is_reconciled_and_forward_installation_can_publish()
    {
        var prepared = await PreparePendingUpdateAsync("pre-marker-full-forward");
        var before = await prepared.Installation.ReadAsync();
        Assert.Equal(
            FeatureAppendStatus.Paused,
            await prepared.Installation.AppendAsync(Input("input-pre-marker-forward-held")));
        var input = new FeatureInput(
            "input-pre-marker-forward-full",
            "active-event",
            "{}",
            fixture.Time.GetUtcNow(),
            "correlation-pre-marker-forward-full",
            "trace-pre-marker-forward-full");

        var delivery = await prepared.Hub.PublishAsync(input);
        var afterDelivery = await prepared.Installation.ReadAsync();

        Assert.Equal(0, delivery.Delivered);
        Assert.Equal(1, delivery.Pending);
        Assert.False(afterDelivery.Paused);
        Assert.Equal(before.Inbox, afterDelivery.Inbox);
        Assert.Equal(before.Revision, afterDelivery.Revision);
        Assert.Null(afterDelivery.PauseReason);
        Assert.DoesNotContain((await prepared.Hub.ReadAsync()).Alerts, alert =>
            alert.InstallationId == prepared.Command.InstallationId && alert.InputId == input.InputId);

        var snapshot = await prepared.Hub.ReadAsync();
        await prepared.Hub.InstallAsync(
            new FeatureInstallationRegistration(
                prepared.Command.InstallationId,
                prepared.Command.Release,
                prepared.Command.Subscriptions),
            snapshot.Revision);
        await fixture.PublishActiveAsync(prepared.Owner, prepared.Hub, prepared.Command.InstallationId);
        var installed = await prepared.Hub.MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
            prepared.Command.DraftId,
            prepared.Command.InstallationId,
            prepared.Command.Release,
            prepared.Command.ExpectedRevision,
            prepared.Command.IdempotencyId,
            fixture.Time.GetUtcNow()));

        Assert.Equal("installed", installed.Status);
        Assert.Equal(prepared.Command.Release, (await prepared.Installation.ReadAsync()).ActiveRelease);
        var replay = await prepared.Hub.PublishAsync(input);
        Assert.Equal(1, replay.Delivered);
        Assert.Equal(0, replay.Pending);
        Assert.Equal(input.InputId, Assert.Single((await prepared.Installation.ReadAsync()).Inbox).InputId);
    }

    [Fact]
    public async Task Reset_reconciles_a_pre_marker_full_pause_to_the_exact_unpaused_baseline()
    {
        var prepared = await PreparePendingUpdateAsync("pre-marker-full-reset");
        Assert.Equal(
            FeatureAppendStatus.Paused,
            await prepared.Installation.AppendAsync(Input("input-pre-marker-reset-full")));
        Assert.False((await prepared.Installation.ReadAsync()).Paused);

        var reset = await prepared.Hub.ResetDraftInstallationReservationAsync(
            new ResetFeatureDraftInstallationReservation(
                prepared.Command.DraftId,
                "reset-pre-marker-full",
                prepared.Command),
            prepared.ActorId);
        var runtime = await prepared.Installation.ReadAsync();

        Assert.True(reset.RequiresRepublish);
        Assert.False(runtime.Paused);
        Assert.Null(runtime.PauseReason);
        Assert.Equal(ReleaseOne, runtime.ActiveRelease);
        await fixture.PublishActiveAsync(prepared.Owner, prepared.Hub, prepared.Command.InstallationId);
        var completed = await prepared.Hub.CompleteDraftInstallationReservationResetAsync(
            prepared.Command.DraftId,
            "reset-pre-marker-full",
            prepared.ActorId);

        Assert.Null(completed.Verification);
        Assert.Null(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        Assert.Null(await prepared.Installation.ReadReservationAsync());
    }

    [Fact]
    public async Task Duplicate_delivery_and_ambiguous_commit_survive_reactivation()
    {
        var installation = Installation("durability");
        await installation.InitializeAsync(ReleaseOne);
        var input = Input("input-durable");

        Assert.Equal(FeatureAppendStatus.Accepted, await installation.AppendAsync(input));
        Assert.Equal(FeatureAppendStatus.Duplicate, await installation.AppendAsync(input));
        var claim = Assert.IsType<FeatureRunClaim>(await installation.ClaimAsync("host-1", TimeSpan.FromSeconds(60)));
        var commit = new FeatureRunCommit(
            claim.Fence,
            "{\"counter\":1}",
            [new FeatureIntent("notify", FeatureIntentKind.Event, "{}")],
            new FeatureResourceUsage(1, 1),
            "{\"ok\":true}");
        var receipt = await installation.CommitAsync(commit);

        await fixture.Cluster.DeactivateAsync((IAddressable)installation);

        Assert.Equal(receipt, await installation.CommitAsync(commit));
        var snapshot = await installation.ReadAsync();
        Assert.Empty(snapshot.Inbox);
        Assert.Single(snapshot.Completions);
        Assert.Single(snapshot.Intents);
        Assert.Equal("{\"counter\":1}", snapshot.StateJson);
    }

    [Fact]
    public async Task Lease_expiry_recovers_a_crashed_claim_and_rejects_its_stale_fence()
    {
        var installation = Installation("lease-expiry");
        await installation.InitializeAsync(ReleaseOne);
        await installation.AppendAsync(Input("input-expiry"));
        var abandoned = Assert.IsType<FeatureRunClaim>(
            await installation.ClaimAsync("host-crashed", TimeSpan.FromSeconds(60)));

        fixture.Time.Advance(TimeSpan.FromSeconds(60));
        var recovered = Assert.IsType<FeatureRunClaim>(
            await installation.ClaimAsync("host-recovery", TimeSpan.FromSeconds(60)));

        Assert.Equal(2, recovered.Attempt);
        Assert.True(recovered.Fence.Fence > abandoned.Fence.Fence);
        await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.CommitAsync(
            new FeatureRunCommit(
                abandoned.Fence,
                "{}",
                [],
                new FeatureResourceUsage(0, 0),
                "{}")));
    }

    [Fact]
    public async Task Duplicate_reminder_delivery_records_at_most_one_downtime_catch_up()
    {
        var installation = Installation("schedule");
        await installation.InitializeAsync(ReleaseOne);
        var now = fixture.Time.GetUtcNow();
        var occurrence = new FeatureScheduleOccurrence(
            "daily-summary",
            now.AddDays(-2),
            now.AddDays(1),
            "{}",
            "correlation-schedule",
            "trace-schedule");

        Assert.Equal(FeatureAppendStatus.Accepted, await installation.RecordScheduleOccurrenceAsync(occurrence));
        Assert.Equal(FeatureAppendStatus.Duplicate, await installation.RecordScheduleOccurrenceAsync(occurrence));

        var snapshot = await installation.ReadAsync();
        Assert.Single(snapshot.Inbox);
        Assert.Single(snapshot.Schedules);
        Assert.Equal(now.AddDays(1), snapshot.Schedules[0].NextOccurrenceAt);
    }

    [Fact]
    public async Task Release_switch_affects_new_claims_and_rollback_restores_the_retained_release()
    {
        var installation = Installation("release-switch");
        await installation.InitializeAsync(ReleaseOne);
        await installation.AppendAsync(Input("input-old"));
        var oldClaim = Assert.IsType<FeatureRunClaim>(
            await installation.ClaimAsync("host-old", TimeSpan.FromSeconds(60)));

        await installation.SwitchReleaseAsync(ReleaseTwo);
        await installation.FailAsync(oldClaim.Fence, fixture.Time.GetUtcNow(), "retry on new release");
        var newClaim = Assert.IsType<FeatureRunClaim>(
            await installation.ClaimAsync("host-new", TimeSpan.FromSeconds(60)));

        Assert.Equal(ReleaseOne, oldClaim.Release);
        Assert.Equal(ReleaseTwo, newClaim.Release);

        await installation.FailAsync(newClaim.Fence, fixture.Time.GetUtcNow(), "rollback");
        await installation.RollbackAsync();
        var rolledBack = Assert.IsType<FeatureRunClaim>(
            await installation.ClaimAsync("host-rollback", TimeSpan.FromSeconds(60)));
        Assert.Equal(ReleaseOne, rolledBack.Release);
    }

    [Fact]
    public async Task Reservation_hold_is_exact_durable_and_blocks_runtime_ingress_and_execution()
    {
        var prepared = await PreparePendingUpdateAsync("reservation-hold");
        var hold = Assert.IsType<FeatureRuntimeReservationSnapshot>(
            await prepared.Installation.ReadReservationAsync());
        var before = await prepared.Installation.ReadAsync();
        var occurrence = new FeatureScheduleOccurrence(
            "reservation-hold-schedule",
            fixture.Time.GetUtcNow(),
            fixture.Time.GetUtcNow().AddHours(1),
            "{}",
            "correlation-reservation-hold",
            "trace-reservation-hold");

        Assert.Equal(FeatureRuntimeReservationPhase.Reserved, hold.Phase);
        Assert.Equal(prepared.Owner, hold.Reservation.OwnerId);
        Assert.Equal(prepared.Command.DraftId, hold.Reservation.DraftId);
        Assert.Equal(prepared.Command.InstallationId, hold.Reservation.InstallationId);
        Assert.Equal(prepared.ActorId, hold.Reservation.ActorId);
        Assert.Equal(FeatureInstallationReservationDigests.Command(prepared.Command), hold.Reservation.ReservationToken);
        Assert.Equal(prepared.Command.Release, hold.Reservation.CandidateRelease);
        Assert.Equal(prepared.Command.RuntimeRevision, hold.Reservation.RuntimeRevision);
        Assert.Equal(FeatureAppendStatus.Paused, await prepared.Installation.AppendAsync(Input("reservation-hold-input")));
        Assert.Equal(FeatureAppendStatus.Paused, await prepared.Installation.RecordScheduleOccurrenceAsync(occurrence));
        Assert.Null(await prepared.Installation.ClaimAsync("reservation-hold-host", TimeSpan.FromSeconds(60)));

        var after = await prepared.Installation.ReadAsync();
        Assert.Equal(before, after);
        await fixture.Cluster.DeactivateAsync((IAddressable)prepared.Installation);
        var replay = await prepared.Hub.AcquireDraftInstallationReservationAsync(prepared.Command, prepared.ActorId);
        var rehydrated = Assert.IsType<FeatureRuntimeReservationSnapshot>(
            await prepared.Installation.ReadReservationAsync());

        Assert.Equal(prepared.Command.DraftId, replay.DraftId);
        Assert.Equal(hold, rehydrated);
        Assert.Null(await prepared.Installation.ClaimAsync("reservation-hold-rehydrated", TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task Persisted_reservation_survives_a_crash_before_the_hold_and_exact_reset_preserves_baseline_work()
    {
        var suffix = "reservation-hold-gap";
        var owner = new BrainOwnerId("owner-" + suffix);
        var installationId = new FeatureInstallationId("installation-" + suffix);
        var actor = new ActorId("actor-" + suffix);
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(ReleaseOne, "sha256:" + ReleaseOne.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            0);
        await hub.DecideAsync(
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseOne, true, "decision-active-" + suffix, actor),
            (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseOne, actor, []),
            (await hub.ReadAsync()).Revision);
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, ["active-event"]),
            (await hub.ReadAsync()).Revision);
        await fixture.PublishActiveAsync(owner, hub, installationId);
        var installation = fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(owner, installationId));
        Assert.Equal(FeatureAppendStatus.Accepted, await installation.AppendAsync(Input("queued-" + suffix)));
        var runtime = await installation.ReadAsync();
        var now = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-" + suffix,
            "Exercise a reservation hold crash boundary",
            now,
            "conversation-" + suffix));
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(ReleaseTwo, draft.Source, 1, now),
            draft.Revision,
            "verification-" + suffix));
        var command = new InstallFeatureVersion(
            draft.DraftId,
            draft.Revision,
            installationId,
            ReleaseTwo,
            [],
            ["candidate-event"],
            "decision-" + suffix,
            "install-" + suffix,
            runtime.Revision,
            runtime.ActiveRelease,
            runtime.PreviousRelease);
        fixture.Storage.FailNextWriteForState("feature-installation-reservation-hold");

        await Assert.ThrowsAsync<OrleansException>(() =>
            hub.AcquireDraftInstallationReservationAsync(command, actor));

        Assert.NotNull(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));
        Assert.Null(await installation.ReadReservationAsync());
        var input = new FeatureInput(
            "input-" + suffix,
            "active-event",
            "{}",
            now,
            "correlation-" + suffix,
            "trace-" + suffix);
        var delivery = await hub.PublishAsync(input);

        Assert.Equal(0, delivery.Delivered);
        Assert.Equal(1, delivery.Pending);
        Assert.Equal("queued-" + suffix, Assert.Single((await installation.ReadAsync()).Inbox).InputId);
        Assert.NotNull(await installation.ClaimAsync("host-" + suffix, TimeSpan.FromSeconds(60)));
        for (var index = 1; index < FeatureLimits.InboxEntries; index++)
            Assert.Equal(FeatureAppendStatus.Accepted, await installation.AppendAsync(Input($"queued-{suffix}-{index}")));
        Assert.Equal(FeatureAppendStatus.Full, await installation.AppendAsync(Input("queued-" + suffix + "-full")));
        var baselineMutation = await installation.ReadAsync();
        Assert.True(baselineMutation.Paused);
        Assert.Equal("feature inbox full", baselineMutation.PauseReason);
        Assert.Equal(FeatureLimits.InboxEntries, baselineMutation.Inbox.Length);

        var reset = await hub.ResetDraftInstallationReservationAsync(
            new ResetFeatureDraftInstallationReservation(command.DraftId, "reset-" + suffix, command),
            actor);

        Assert.True(reset.Completed);
        Assert.Null(await hub.ReadDraftInstallationReservationAsync(command.DraftId));
        Assert.Null(await installation.ReadReservationAsync());
        var afterReset = await installation.ReadAsync();
        Assert.Equal(baselineMutation.Revision + 1, afterReset.Revision);
        Assert.Equal(baselineMutation.ActiveRelease, afterReset.ActiveRelease);
        Assert.Equal(baselineMutation.PreviousRelease, afterReset.PreviousRelease);
        Assert.Equal(baselineMutation.Lease, afterReset.Lease);
        Assert.False(afterReset.Paused);
        Assert.Null(afterReset.PauseReason);
        Assert.Equal(baselineMutation.Inbox, afterReset.Inbox);
    }

    [Fact]
    public async Task Resetting_hold_retry_accepts_an_exact_live_baseline_after_the_preparation_hub_write_fails()
    {
        var prepared = await PreparePendingUpdateAsync("resetting-hold-hub-failure");
        var reservation = Assert.IsType<FeatureDraftInstallationReservation>(
            await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        var runtimeReservation = RuntimeReservation(prepared.Owner, reservation);
        await prepared.Installation.ReleaseReservationAsync(new FeatureRuntimeReservationRelease(
            runtimeReservation,
            FeatureRuntimeReservationPhase.Reserved,
            reservation.RuntimeActiveRelease,
            reservation.RuntimePreviousRelease,
            false));
        Assert.Null(await prepared.Installation.ReadReservationAsync());
        Assert.Equal(
            FeatureAppendStatus.Accepted,
            await prepared.Installation.AppendAsync(Input("resetting-hold-live")));
        Assert.NotNull(await prepared.Installation.ClaimAsync(
            "host-resetting-hold-live",
            TimeSpan.FromSeconds(60)));
        for (var index = 1; index < FeatureLimits.InboxEntries; index++)
        {
            Assert.Equal(
                FeatureAppendStatus.Accepted,
                await prepared.Installation.AppendAsync(Input($"resetting-hold-live-{index}")));
        }
        Assert.Equal(
            FeatureAppendStatus.Full,
            await prepared.Installation.AppendAsync(Input("resetting-hold-live-full")));
        var beforeReset = await prepared.Installation.ReadAsync();
        Assert.True(beforeReset.Paused);
        Assert.Equal("feature inbox full", beforeReset.PauseReason);
        Assert.NotNull(beforeReset.Lease);
        Assert.Equal(FeatureLimits.InboxEntries, beforeReset.Inbox.Length);
        var command = new ResetFeatureDraftInstallationReservation(
            prepared.Command.DraftId,
            "resetting-hold-hub-failure",
            prepared.Command);
        fixture.Storage.FailNextWriteForState("feature-hub");

        await Assert.ThrowsAsync<OrleansException>(() =>
            prepared.Hub.ResetDraftInstallationReservationAsync(command, prepared.ActorId));

        var afterFailure = await prepared.Installation.ReadAsync();
        var resetting = Assert.IsType<FeatureRuntimeReservationSnapshot>(
            await prepared.Installation.ReadReservationAsync());
        Assert.Equal(FeatureRuntimeReservationPhase.Resetting, resetting.Phase);
        Assert.Equal(runtimeReservation, resetting.Reservation);
        Assert.False(afterFailure.Paused);
        Assert.Null(afterFailure.PauseReason);
        Assert.Equal(beforeReset.Revision + 1, afterFailure.Revision);
        Assert.Equal(beforeReset.Lease, afterFailure.Lease);
        Assert.Equal(beforeReset.Inbox, afterFailure.Inbox);
        Assert.Equal(beforeReset.StateJson, afterFailure.StateJson);

        var retried = await prepared.Hub.ResetDraftInstallationReservationAsync(command, prepared.ActorId);
        var afterRetry = await prepared.Installation.ReadAsync();

        Assert.False(retried.Completed);
        Assert.True(retried.RequiresRepublish);
        Assert.NotNull(await prepared.Hub.ReadDraftInstallationReservationAsync(prepared.Command.DraftId));
        Assert.NotNull(await prepared.Hub.ReadDraftInstallationResetAsync(prepared.Command.DraftId));
        Assert.Equal(afterFailure.Revision, afterRetry.Revision);
        Assert.Equal(afterFailure.Lease, afterRetry.Lease);
        Assert.Equal(afterFailure.Inbox, afterRetry.Inbox);
        Assert.Equal(afterFailure.StateJson, afterRetry.StateJson);
    }

    [Fact]
    public async Task Release_switch_waits_for_an_active_lease_normalizes_expiry_and_never_deadlocks_a_full_inbox()
    {
        var installation = Installation("release-switch-quiescence");
        await installation.InitializeAsync(ReleaseOne);
        await installation.AppendAsync(Input("input-release-switch-lease"));
        var claimed = Assert.IsType<FeatureRunClaim>(
            await installation.ClaimAsync("host-release-switch", TimeSpan.FromSeconds(60)));
        var before = await installation.ReadAsync();

        var activeLease = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            installation.BeginReleaseSwitchAsync(ReleaseTwo, "switch-quiescence"));
        Assert.Equal(FeatureCommandRejectionReason.Precondition, activeLease.Reason);
        var afterRejection = await installation.ReadAsync();
        Assert.Equal(before.ActiveRelease, afterRejection.ActiveRelease);
        Assert.Equal(before.PreviousRelease, afterRejection.PreviousRelease);
        Assert.Equal(before.Revision, afterRejection.Revision);
        Assert.Equal(before.Lease, afterRejection.Lease);
        Assert.Null(afterRejection.UnconfirmedReleaseSwitch);

        fixture.Time.Advance(TimeSpan.FromSeconds(60));
        await installation.BeginReleaseSwitchAsync(ReleaseTwo, "switch-quiescence");
        var switched = await installation.ReadAsync();

        Assert.Null(switched.Lease);
        Assert.Equal(ReleaseTwo, switched.ActiveRelease);
        Assert.Equal(ReleaseOne, switched.PreviousRelease);
        Assert.Equal(before.Revision + 2, switched.Revision);
        Assert.Equal(before.Revision + 1, switched.UnconfirmedReleaseSwitch?.FromRevision);
        Assert.Equal(before.Revision + 2, switched.UnconfirmedReleaseSwitch?.SwitchRevision);
        Assert.Null(await installation.ClaimAsync("host-quiescent", TimeSpan.FromSeconds(60)));
        Assert.Equal(
            FeatureCommandRejectionReason.Conflict,
            (await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.PauseAsync("blocked"))).Reason);
        Assert.Equal(
            FeatureCommandRejectionReason.Conflict,
            (await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.ResumeAsync())).Reason);
        Assert.Equal(
            FeatureCommandRejectionReason.Conflict,
            (await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.SwitchReleaseAsync(ReleaseOne))).Reason);
        Assert.Equal(
            FeatureCommandRejectionReason.Conflict,
            (await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.RollbackAsync())).Reason);

        for (var index = 1; index < FeatureLimits.InboxEntries; index++)
            Assert.Equal(FeatureAppendStatus.Accepted, await installation.AppendAsync(Input($"input-quiescent-{index}")));
        var full = await installation.ReadAsync();
        Assert.Equal(FeatureLimits.InboxEntries, full.Inbox.Length);
        Assert.Equal(FeatureAppendStatus.Full, await installation.AppendAsync(Input("input-quiescent-full")));
        var occurrence = new FeatureScheduleOccurrence(
            "quiescent-full",
            fixture.Time.GetUtcNow(),
            fixture.Time.GetUtcNow().AddHours(1),
            "{}",
            "correlation-quiescent-full",
            "trace-quiescent-full");
        Assert.Equal(FeatureAppendStatus.Full, await installation.RecordScheduleOccurrenceAsync(occurrence));
        var afterFull = await installation.ReadAsync();

        Assert.False(afterFull.Paused);
        Assert.Equal(full.Revision, afterFull.Revision);
        Assert.Equal(full.UnconfirmedReleaseSwitch, afterFull.UnconfirmedReleaseSwitch);
        Assert.Empty(afterFull.Schedules);

        await installation.ConfirmReleaseSwitchAsync(ReleaseTwo);
        var forwardClaim = Assert.IsType<FeatureRunClaim>(
            await installation.ClaimAsync("host-forward", TimeSpan.FromSeconds(60)));
        Assert.Equal(claimed.Fence.InputId, forwardClaim.Fence.InputId);
        Assert.Equal(ReleaseTwo, forwardClaim.Release);
        await installation.CommitAsync(new FeatureRunCommit(
            forwardClaim.Fence,
            "{}",
            [],
            new FeatureResourceUsage(0, 0),
            "{}"));
        Assert.Equal(FeatureAppendStatus.Accepted, await installation.AppendAsync(Input("input-quiescent-full")));
    }

    [Fact]
    public async Task One_paused_recipient_does_not_block_independent_fan_out()
    {
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(Owner));
        var paused = Installation("fanout-paused");
        var healthy = Installation("fanout-healthy");
        await paused.InitializeAsync(ReleaseOne);
        await healthy.InitializeAsync(ReleaseOne);
        await paused.PauseAsync("operator hold");
        await hub.RegisterAsync(new FeatureInstallationRegistration(new("fanout-paused"), ReleaseOne, ["email.received"]));
        await hub.RegisterAsync(new FeatureInstallationRegistration(new("fanout-healthy"), ReleaseOne, ["email.received"]));

        var result = await hub.PublishAsync(Input("input-fanout"));

        Assert.Equal(1, result.Delivered);
        Assert.Equal(1, result.Pending);
        Assert.Empty((await paused.ReadAsync()).Inbox);
        Assert.Single((await healthy.ReadAsync()).Inbox);
    }

    [Fact]
    public async Task Installation_state_rehydrates_after_a_silo_restart()
    {
        var installation = Installation("silo-restart");
        await installation.InitializeAsync(ReleaseOne);
        await installation.AppendAsync(Input("input-restart"));
        var before = await installation.ReadAsync();

        var silo = Assert.Single(fixture.Cluster.Silos);
        var restarted = await fixture.Cluster.RestartSiloAsync(silo);
        Assert.NotNull(restarted);
        await fixture.Cluster.WaitForLivenessToStabilizeAsync();

        var after = await installation.ReadAsync();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(Assert.Single(before.Inbox), Assert.Single(after.Inbox));
        Assert.Equal(before.ActiveRelease, after.ActiveRelease);
    }

    [Fact]
    public async Task Failed_persistence_does_not_leak_an_uncommitted_success_into_the_activation()
    {
        var installation = Installation("write-failure");
        await installation.InitializeAsync(ReleaseOne);
        fixture.Storage.FailNextWrite();

        await Assert.ThrowsAsync<OrleansException>(() => installation.AppendAsync(Input("input-write-failure")));

        Assert.Empty((await installation.ReadAsync()).Inbox);
    }

    [Fact]
    public async Task First_installation_write_failure_preserves_the_storage_failure()
    {
        var installation = Installation("first-installation-write-failure");
        fixture.Storage.FailNextWrite();

        var failure = await Assert.ThrowsAsync<OrleansException>(() => installation.InitializeAsync(ReleaseOne));

        Assert.DoesNotContain(nameof(NullReferenceException), failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task First_hub_write_failure_preserves_the_storage_failure()
    {
        var owner = new BrainOwnerId("owner-first-hub-write-failure");
        var installationId = new FeatureInstallationId("first-hub-write-failure");
        var installation = fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(owner, installationId));
        await installation.InitializeAsync(ReleaseOne);
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        fixture.Storage.FailNextWrite();

        var failure = await Assert.ThrowsAsync<OrleansException>(() => hub.RegisterAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, ["email.received"])));

        Assert.DoesNotContain(nameof(NullReferenceException), failure.ToString(), StringComparison.Ordinal);
        Assert.Empty((await hub.ReadAsync()).Installations);
    }

    [Fact]
    public async Task A_post_commit_storage_exception_reconciles_to_the_durable_success()
    {
        var installation = Installation("write-acknowledgement-failure");
        await installation.InitializeAsync(ReleaseOne);
        fixture.Storage.CommitThenFailNextWrite();

        Assert.Equal(
            FeatureAppendStatus.Accepted,
            await installation.AppendAsync(Input("input-write-acknowledgement-failure")));
        Assert.Single((await installation.ReadAsync()).Inbox);
    }

    [Fact]
    public async Task Attempt_limit_parking_is_persisted_when_claim_returns_no_work()
    {
        var installation = Installation("attempt-limit-parking");
        await installation.InitializeAsync(ReleaseOne);
        await installation.AppendAsync(Input("input-attempt-limit"));
        for (var attempt = 1; attempt <= FeatureLimits.AttemptsPerInput; attempt++)
        {
            Assert.Equal(
                attempt,
                Assert.IsType<FeatureRunClaim>(await installation.ClaimAsync(
                    $"host-{attempt}",
                    TimeSpan.FromSeconds(60))).Attempt);
            fixture.Time.Advance(TimeSpan.FromSeconds(60));
        }

        Assert.Null(await installation.ClaimAsync("host-overflow", TimeSpan.FromSeconds(60)));
        await fixture.Cluster.DeactivateAsync((IAddressable)installation);

        var snapshot = await installation.ReadAsync();
        Assert.True(snapshot.Paused);
        Assert.Equal("input-attempt-limit", Assert.Single(snapshot.Inbox).InputId);
        Assert.Equal("The feature host attempt limit was reached.", snapshot.PauseReason);
    }

    [Fact]
    public async Task Competing_same_revision_state_is_not_mistaken_for_the_attempted_write()
    {
        var installation = Installation("same-revision-conflict");
        await installation.InitializeAsync(ReleaseOne);
        fixture.Storage.CommitCompetingStateThenFailNextWrite(state =>
            ((FeatureInstallationState)state) with { StateJson = "{\"competing\":true}" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => installation.AppendAsync(Input("input-same-revision")));

        Assert.Equal("{\"competing\":true}", (await installation.ReadAsync()).StateJson);
    }

    [Fact]
    public async Task Rollback_is_idempotent_after_an_ambiguous_storage_response()
    {
        var installation = Installation("rollback-ambiguous");
        await installation.InitializeAsync(ReleaseOne);
        await installation.SwitchReleaseAsync(ReleaseTwo);
        fixture.Storage.CommitThenFailNextWrite();

        await installation.RollbackAsync();
        await installation.RollbackAsync();

        var snapshot = await installation.ReadAsync();
        Assert.Equal(ReleaseOne, snapshot.ActiveRelease);
        Assert.Null(snapshot.PreviousRelease);
    }

    [Fact]
    public async Task Runtime_grant_source_revalidates_actor_connection_revision_revoke_and_pause_on_every_read()
    {
        var owner = new BrainOwnerId("owner-live-grant");
        var actor = new ActorId("actor-live-grant");
        var installationId = new FeatureInstallationId("live-grant");
        var connection = new ProviderConnectionId("google-live-grant");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var proposal = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(
                    ReleaseOne,
                    "sha256:" + ReleaseOne.Value,
                    FeatureSourceKind.RuntimeAuthored,
                    ["gmail.message.read.v1"],
                    ["DigitalBrain.Integrations.Google.Contracts"]),
                [new("gmail.message.read.v1", 1, connection, "{\"allowedToolIds\":[\"gmail.message.read.v1\"]}", "google")]),
            0);
        await hub.DecideAsync(
            new FeatureApprovalDecision(proposal.ApprovalId, ReleaseOne, true, "decision-live-grant", actor),
            1);
        var authority = await hub.GrantAsync(
            new FeatureGrantRequest(
                installationId,
                ReleaseOne,
                actor,
                [new("gmail.message.read.v1", 1, connection, "{\"allowedToolIds\":[\"gmail.message.read.v1\"]}", "google")]),
            2);
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, ["gmail.message.received.v1"]),
            3);
        var source = new FeatureCapabilityGrantSource(fixture.Cluster.Client);
        CapabilityRequest Request(ActorId requestActor, ProviderConnectionId? requestConnection, GrantRevision revision) => new(
            owner,
            requestActor,
            installationId,
            ReleaseOne,
            "input-live-grant",
            "read-message",
            "gmail.message.read.v1",
            1,
            requestConnection,
            revision,
            JsonSerializer.SerializeToElement(new { messageId = "message-1" }),
            fixture.Time.GetUtcNow().AddSeconds(30),
            "correlation-live-grant",
            null);
        var grantRevision = Assert.IsType<GrantRevision>(authority.PendingGrantRevision);
        var request = Request(actor, connection, grantRevision);

        Assert.NotNull(await source.ReadAsync(request));
        Assert.Null(await source.ReadAsync(Request(new ActorId("another-actor"), connection, grantRevision)));
        Assert.Null(await source.ReadAsync(Request(actor, new ProviderConnectionId("another-connection"), grantRevision)));
        Assert.Null(await source.ReadAsync(Request(actor, connection, new GrantRevision(grantRevision.Value + 1))));

        await hub.PauseInstallationAsync(installationId, "operator hold", (await hub.ReadAsync()).Revision);
        Assert.Null(await source.ReadAsync(request));
        await hub.ResumeInstallationAsync(installationId, (await hub.ReadAsync()).Revision);
        Assert.NotNull(await source.ReadAsync(request));
        await hub.RevokeAsync(
            new FeatureGrantRevocation(installationId, ReleaseOne, "gmail.message.read.v1", 1),
            (await hub.ReadAsync()).Revision);
        Assert.Null(await source.ReadAsync(request));
    }

    [Fact]
    public async Task Invalid_registration_is_rejected_before_initializing_the_installation()
    {
        var installationId = new FeatureInstallationId("invalid-registration");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(new BrainOwnerId("owner-invalid-registration")));

        await Assert.ThrowsAsync<ArgumentException>(() => hub.RegisterAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, [])));

        var installation = fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(
            new BrainOwnerId("owner-invalid-registration"),
            installationId));
        await installation.InitializeAsync(ReleaseTwo);
        Assert.Equal(ReleaseTwo, (await installation.ReadAsync()).ActiveRelease);
    }

    [Fact]
    public async Task Installation_grain_expected_state_failures_are_typed_preconditions()
    {
        var owner = new BrainOwnerId("owner-installation-preconditions");
        var installation = fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(
            owner,
            new FeatureInstallationId("installation-preconditions")));

        var uninitialized = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => installation.ReadAsync());
        await installation.InitializeAsync(ReleaseOne);
        var anotherRelease = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            installation.InitializeAsync(ReleaseTwo));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, uninitialized.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, anotherRelease.Reason);
    }

    [Fact]
    public async Task Hub_install_rejects_a_staged_release_mismatch_as_a_typed_precondition()
    {
        var owner = new BrainOwnerId("owner-install-release-precondition");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var installationId = new FeatureInstallationId("installation-release-precondition");
        var release = new FeatureReleaseMetadata(
            ReleaseOne,
            "sha256:" + ReleaseOne.Value,
            FeatureSourceKind.RuntimeAuthored,
            [],
            []);
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(installationId, release, []),
            (await hub.ReadAsync()).Revision);
        await hub.DecideAsync(
            new FeatureApprovalDecision(
                approval.ApprovalId,
                ReleaseOne,
                true,
                "decision-release-precondition",
                new ActorId("actor-release-precondition")),
            (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseOne, new ActorId("actor-release-precondition"), []),
            (await hub.ReadAsync()).Revision);
        var revision = (await hub.ReadAsync()).Revision;

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, ReleaseTwo, ["manual"]),
            revision));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
    }

    [Fact]
    public async Task Hub_second_install_projects_exact_rollback_availability()
    {
        var owner = new BrainOwnerId("owner-exact-rollback-availability");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var installationId = new FeatureInstallationId("installation-exact-rollback-availability");
        var firstGrant = new FeatureGrantSpec(
            "capability.first",
            1,
            null,
            JsonSerializer.Serialize(new { allowedToolIds = new[] { "capability.first" } }));
        var secondGrant = new FeatureGrantSpec(
            "capability.second",
            1,
            null,
            JsonSerializer.Serialize(new { allowedToolIds = new[] { "capability.second" } }));

        await InstallHubReleaseAsync(hub, installationId, ReleaseOne, firstGrant, ["first"], "availability-first");
        var first = Assert.Single((await hub.ReadAsync()).Authorities);
        await InstallHubReleaseAsync(hub, installationId, ReleaseTwo, secondGrant, ["second"], "availability-second");
        var second = Assert.Single((await hub.ReadAsync()).Authorities);

        Assert.False(first.ExactRollbackAvailable);
        Assert.True(second.ExactRollbackAvailable);
    }

    [Fact]
    public async Task Hub_exact_rollback_restores_authority_registration_and_runtime_once()
    {
        var owner = new BrainOwnerId("owner-exact-rollback");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var installationId = new FeatureInstallationId("installation-exact-rollback");
        var firstGrant = new FeatureGrantSpec(
            "capability.first",
            1,
            null,
            JsonSerializer.Serialize(new { allowedToolIds = new[] { "capability.first" } }));
        var secondGrant = new FeatureGrantSpec(
            "capability.second",
            1,
            null,
            JsonSerializer.Serialize(new { allowedToolIds = new[] { "capability.second" } }));
        await InstallHubReleaseAsync(hub, installationId, ReleaseOne, firstGrant, ["z-first", "a-first"], "first");
        await InstallHubReleaseAsync(hub, installationId, ReleaseTwo, secondGrant, ["second"], "second");
        await fixture.PublishActiveAsync(owner, hub, installationId);
        var before = await hub.ReadAsync();
        var command = new RollbackFeatureInstallation(
            installationId,
            ReleaseTwo,
            ReleaseOne,
            before.Revision,
            "rollback-exact-grain");

        var authority = await hub.RollbackInstallationExactAsync(command);
        var rolledBack = await hub.ReadAsync();
        await fixture.PublishActiveAsync(owner, hub, installationId);
        var published = await hub.ReadAsync();
        var replay = await hub.RollbackInstallationExactAsync(command);
        var replayed = await hub.ReadAsync();
        var ticket = await hub.PrepareActivePublicationAsync(installationId);
        var runtime = await fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(owner, installationId)).ReadAsync();

        Assert.Equal(ReleaseOne, authority.ActiveRelease);
        Assert.Equal(["capability.first"], authority.ActiveGrants.Select(grant => grant.CapabilityId));
        Assert.Null(authority.PreviousRelease);
        Assert.Equal(authority.ActiveRelease, replay.ActiveRelease);
        Assert.Equal(authority.ActiveGrantRevision, replay.ActiveGrantRevision);
        Assert.Equal(
            authority.ActiveGrants.Select(grant => grant.CapabilityId),
            replay.ActiveGrants.Select(grant => grant.CapabilityId));
        var registration = Assert.Single(rolledBack.Installations);
        Assert.Equal(ReleaseOne, registration.Release);
        Assert.Equal(["a-first", "z-first"], registration.Subscriptions);
        Assert.Equal(ReleaseOne, runtime.ActiveRelease);
        Assert.Null(runtime.PreviousRelease);
        Assert.Equal(ReleaseOne, ticket.Release);
        Assert.Equal(["capability.first"], ticket.ActiveGrants.Select(grant => grant.CapabilityId));
        Assert.Equal(["a-first", "z-first"], ticket.Subscriptions);
        Assert.Equal(published.Revision, replayed.Revision);
        Assert.True(published.Revision > rolledBack.Revision);
    }

    [Fact]
    public void Feature_runtime_declares_exactly_two_non_reentrant_grain_types()
    {
        var grainTypes = typeof(FeatureInstallationGrain).Assembly.GetTypes()
            .Where(type => type.Namespace == "DigitalBrain.Kernel.Features" &&
                !type.IsAbstract &&
                typeof(Grain).IsAssignableFrom(type))
            .ToArray();

        Assert.Equal(
            [typeof(FeatureHubGrain), typeof(FeatureInstallationGrain)],
            grainTypes.OrderBy(type => type.Name).ToArray());
        Assert.All(grainTypes, type => Assert.Null(type.GetCustomAttributes(typeof(ReentrantAttribute), false).SingleOrDefault()));
    }

    [Fact]
    public void Feature_grains_use_generated_state_serialization_and_only_approved_runtime_dependencies()
    {
        Type[] persistedTypes =
        [
            typeof(FeatureHubState),
            typeof(FeatureDraftCommandReplay),
            typeof(FeatureRollbackReplay),
            typeof(FeatureFanOutState),
            typeof(FeatureFanOutDeliveryState),
            typeof(FeatureInstallationState),
            typeof(FeatureInboxEntry),
            typeof(FeatureLease),
            typeof(FeatureCompletion),
            typeof(PersistedFeatureIntent),
            typeof(FeatureScheduleCursor)
        ];
        Assert.All(persistedTypes, type => Assert.NotNull(
            type.GetCustomAttributes(typeof(GenerateSerializerAttribute), false).SingleOrDefault()));

        var dependencies = new[] { typeof(FeatureHubGrain), typeof(FeatureInstallationGrain) }
            .SelectMany(type => Assert.Single(type.GetConstructors()).GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.All(dependencies, dependency => Assert.True(
            dependency == typeof(IGrainFactory) ||
            dependency == typeof(TimeProvider) ||
            dependency == typeof(IFeaturePublicationVerifier) ||
            dependency.IsGenericType && dependency.GetGenericTypeDefinition() == typeof(IPersistentState<>),
            $"Unexpected feature grain dependency: {dependency}."));
    }

    private async Task<(BrainOwnerId Owner, IFeatureHubGrain Hub, InstallFeatureVersion Command, ActorId ActorId)>
        PreparePendingDraftInstallationAsync(string suffix)
    {
        var owner = new BrainOwnerId("owner-" + suffix);
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var now = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-" + suffix,
            "Prepare a resettable Feature",
            now,
            "conversation-" + suffix));
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(ReleaseOne, draft.Source, 1, now),
            draft.Revision,
            "verification-" + suffix));
        var actor = new ActorId("actor-" + suffix);
        var command = new InstallFeatureVersion(
            draft.DraftId,
            draft.Revision,
            new FeatureInstallationId("installation-" + suffix),
            ReleaseOne,
            [],
            ["manual"],
            "decision-" + suffix,
            "install-" + suffix);
        await hub.AcquireDraftInstallationReservationAsync(command, actor);
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                command.InstallationId,
                new FeatureReleaseMetadata(ReleaseOne, "sha256:" + ReleaseOne.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            (await hub.ReadAsync()).Revision);
        await hub.DecideAsync(
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseOne, true, command.DecisionId, actor),
            (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(command.InstallationId, ReleaseOne, actor, []),
            (await hub.ReadAsync()).Revision);
        return (owner, hub, command, actor);
    }

    private async Task<(
        BrainOwnerId Owner,
        IFeatureHubGrain Hub,
        IFeatureInstallationGrain Installation,
        InstallFeatureVersion Command,
        ActorId ActorId)> PreparePendingUpdateAsync(string suffix, ReleaseDigest? candidateRelease = null)
    {
        var owner = new BrainOwnerId("owner-" + suffix);
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var installationId = new FeatureInstallationId("installation-" + suffix);
        var actor = new ActorId("actor-" + suffix);
        var activeDecisionId = "decision-active-" + suffix;
        var activeApproval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(ReleaseOne, "sha256:" + ReleaseOne.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            0);
        await hub.DecideAsync(
            new FeatureApprovalDecision(activeApproval.ApprovalId, ReleaseOne, true, activeDecisionId, actor),
            (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseOne, actor, []),
            (await hub.ReadAsync()).Revision);
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, ReleaseOne, ["active-event"]),
            (await hub.ReadAsync()).Revision);
        await fixture.PublishActiveAsync(owner, hub, installationId);
        var installation = fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(owner, installationId));
        var runtime = await installation.ReadAsync();
        var release = candidateRelease ?? ReleaseTwo;
        string[] subscriptions = release == ReleaseOne ? ["active-event"] : ["candidate-event"];
        var decisionId = release == ReleaseOne ? activeDecisionId : "decision-" + suffix;
        var now = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-" + suffix,
            "Prepare a resettable Feature update",
            now,
            "conversation-" + suffix));
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(release, draft.Source, 1, now),
            draft.Revision,
            "verification-" + suffix));
        var command = new InstallFeatureVersion(
            draft.DraftId,
            draft.Revision,
            installationId,
            release,
            [],
            subscriptions,
            decisionId,
            "install-" + suffix,
            runtime.Revision,
            runtime.ActiveRelease,
            runtime.PreviousRelease);
        await hub.AcquireDraftInstallationReservationAsync(command, actor);
        var candidateApproval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(release, "sha256:" + release.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            (await hub.ReadAsync()).Revision);
        if (release != ReleaseOne)
            await hub.DecideAsync(
                new FeatureApprovalDecision(candidateApproval.ApprovalId, release, true, command.DecisionId, actor),
                (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, release, actor, []),
            (await hub.ReadAsync()).Revision);
        return (owner, hub, installation, command, actor);
    }

    private IFeatureInstallationGrain Installation(string installationId) =>
        fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(Owner, new FeatureInstallationId(installationId)));

    private static FeatureRuntimeReservation RuntimeReservation(
        BrainOwnerId ownerId,
        FeatureDraftInstallationReservation reservation) =>
        new(
            ownerId,
            reservation.DraftId,
            reservation.InstallationId,
            reservation.ActorId,
            reservation.CommandDigest,
            reservation.AccessDigest,
            reservation.Release,
            reservation.RuntimeRevision,
            reservation.RuntimeActiveRelease,
            reservation.RuntimePreviousRelease,
            reservation.AuthorityBaseline?.Paused,
            reservation.AuthorityBaseline?.PauseReason);

    private static async Task InstallHubReleaseAsync(
        IFeatureHubGrain hub,
        FeatureInstallationId installationId,
        ReleaseDigest release,
        FeatureGrantSpec grant,
        string[] subscriptions,
        string suffix)
    {
        var metadata = new FeatureReleaseMetadata(
            release,
            "sha256:" + release.Value,
            FeatureSourceKind.RuntimeAuthored,
            [grant.CapabilityId],
            []);
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(installationId, metadata, [grant]),
            (await hub.ReadAsync()).Revision);
        await hub.DecideAsync(
            new FeatureApprovalDecision(
                approval.ApprovalId,
                release,
                true,
                "decision-" + suffix,
                new ActorId("actor-exact-rollback")),
            (await hub.ReadAsync()).Revision);
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, release, new ActorId("actor-exact-rollback"), [grant]),
            (await hub.ReadAsync()).Revision);
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, release, subscriptions),
            (await hub.ReadAsync()).Revision);
    }

    private static FeatureSourceSnapshot GrainSource() => new(
        "src/Feature/Feature.csproj",
        "tests/Feature.Scenarios/Feature.Scenarios.csproj",
        [
            new FeatureSourceFile("src/Feature/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
        ]);

    private static FeatureHubState DeepClone(FeatureHubState state)
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder
            .AddAssembly(typeof(FeatureDraft).Assembly)
            .AddAssembly(typeof(FeatureHubState).Assembly));
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer<FeatureHubState>>();
        return serializer.Deserialize(serializer.SerializeToArray(state));
    }

    private static FeatureInput Input(string inputId) => new(
        inputId,
        "email.received",
        "{}",
        new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero),
        $"correlation-{inputId}",
        $"trace-{inputId}");
}
