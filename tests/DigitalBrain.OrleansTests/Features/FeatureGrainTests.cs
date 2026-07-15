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
        var verification = new FeatureVerification(ReleaseOne, 1, 1, 0, 0, createdAt.AddMinutes(3));
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
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseOne, true, "decision-installed-grain"),
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.ReviseSourceAsync(new ReviseFeatureSource(
            draft.DraftId,
            GrainSource(),
            4,
            "source-after-install",
            createdAt.AddMinutes(5))));
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
            new FeatureVerification(ReleaseOne, 1, 1, 0, 0, now),
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
            "install-forged-publication"), new ActorId("actor-forged-publication"));
        var snapshot = await hub.ReadAsync();
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(ReleaseOne, "sha256:" + ReleaseOne.Value, FeatureSourceKind.RuntimeAuthored, [], []),
                []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.DecideAsync(
            new FeatureApprovalDecision(approval.ApprovalId, ReleaseOne, true, "decision-forged-publication"),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, ReleaseOne, new ActorId("actor-forged-publication"), []),
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.ConfirmActivePublicationAsync(forged));
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
            draft.DraftId,
            installationId,
            ReleaseOne,
            draft.Revision,
            "install-forged-publication",
            now.AddMinutes(1))));

        Assert.Equal("draft", (await hub.ReadDraftAsync(draft.DraftId))?.Status);
        Assert.NotNull(await hub.ReadDraftInstallationReservationAsync(draft.DraftId));
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => installation.CommitAsync(
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
            new FeatureApprovalDecision(proposal.ApprovalId, ReleaseOne, true, "decision-live-grant"),
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

    private IFeatureInstallationGrain Installation(string installationId) =>
        fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(Owner, new FeatureInstallationId(installationId)));

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
