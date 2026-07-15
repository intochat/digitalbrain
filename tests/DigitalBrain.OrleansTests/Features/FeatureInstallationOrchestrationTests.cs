extern alias McpProject;

using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using FeatureArtifactCatalog = McpProject::DigitalBrain.Mcp.IFeatureArtifactCatalog;
using FeatureAuthoringService = McpProject::DigitalBrain.Mcp.FeatureAuthoringService;
using FeatureBuildArtifact = McpProject::DigitalBrain.Mcp.FeatureBuildArtifact;
using FeatureBuildEndpoint = McpProject::DigitalBrain.Mcp.IFeatureBuildEndpoint;
using FeatureBuildSubmission = McpProject::DigitalBrain.Mcp.FeatureBuildSubmission;
using FeatureInstallationInspection = McpProject::DigitalBrain.Mcp.FeatureInstallationInspection;
using FeatureLifecycleInspection = McpProject::DigitalBrain.Mcp.FeatureLifecycleInspection;
using FeatureLifecycleRail = McpProject::DigitalBrain.Mcp.IFeatureLifecycleRail;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.OrleansTests.Features;

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class FeatureInstallationOrchestrationTests(FeatureGrainClusterFixture fixture)
{
    [Fact]
    public async Task Preparing_Access_Review_is_trusted_and_nonmutating()
    {
        var setup = await SetupAsync("review");

        var review = await setup.Service.PrepareAccessReviewAsync(setup.Context, new PrepareFeatureAccessReview(
            setup.Draft.DraftId,
            setup.Draft.Revision,
            setup.InstallationId,
            setup.Release.Digest,
            setup.Grants,
            setup.Subscriptions));

        Assert.Equal(setup.Release, review.Candidate.Release);
        Assert.Equal(setup.Draft.DraftId, review.Candidate.Draft.DraftId);
        Assert.Equal(setup.Grants, review.Grants);
        Assert.Equal(setup.Subscriptions, review.Subscriptions);
        Assert.Equal(0, setup.Lifecycle.MutationCount);
        Assert.Null((await setup.Hub.ReadDraftAsync(setup.Draft.DraftId))?.InstallationId);
    }

    [Fact]
    public async Task Digest_and_complete_grant_mismatches_are_rejected_before_lifecycle_mutation()
    {
        var setup = await SetupAsync("mismatch");
        var otherDigest = Digest('b');

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.PrepareAccessReviewAsync(setup.Context, new PrepareFeatureAccessReview(
            setup.Draft.DraftId,
            setup.Draft.Revision,
            setup.InstallationId,
            otherDigest,
            setup.Grants,
            setup.Subscriptions)));
        Assert.Equal(0, setup.Catalog.CallCount);

        var mismatches = new[]
        {
            Array.Empty<FeatureGrantSpec>(),
            new[] { setup.Grants[0], new FeatureGrantSpec("capability.extra", 1, null, "{}") },
            new[] { setup.Grants[0], setup.Grants[0] }
        };
        foreach (var grants in mismatches)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => setup.Service.PrepareAccessReviewAsync(setup.Context, new PrepareFeatureAccessReview(
                setup.Draft.DraftId,
                setup.Draft.Revision,
                setup.InstallationId,
                setup.Release.Digest,
                grants,
                setup.Subscriptions)));
        }

        Assert.Equal(0, setup.Lifecycle.MutationCount);
    }

    [Fact]
    public async Task Access_review_rejects_constraints_that_the_authority_domain_cannot_install()
    {
        var setup = await SetupAsync("constraint-policy");

        await Assert.ThrowsAsync<ArgumentException>(() => setup.Service.PrepareAccessReviewAsync(setup.Context, new PrepareFeatureAccessReview(
            setup.Draft.DraftId,
            setup.Draft.Revision,
            setup.InstallationId,
            setup.Release.Digest,
            [setup.Grants[0] with { ConstraintsJson = "{\"scope\":\"read\"}" }],
            setup.Subscriptions)));

        Assert.Equal(0, setup.Lifecycle.MutationCount);
    }

    [Fact]
    public async Task A_registration_only_coordinate_prevents_release_retargeting()
    {
        var setup = await SetupAsync("registration-coordinate");
        setup.Lifecycle.RegistrationOnly = new FeatureInstallationRegistration(
            new FeatureInstallationId("installation-registration-other"),
            setup.Release.Digest,
            setup.Subscriptions);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.PrepareAccessReviewAsync(setup.Context, new PrepareFeatureAccessReview(
            setup.Draft.DraftId,
            setup.Draft.Revision,
            setup.InstallationId,
            setup.Release.Digest,
            setup.Grants,
            setup.Subscriptions)));

        Assert.Equal(0, setup.Lifecycle.MutationCount);
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("registration")]
    [InlineData("active-authority")]
    public async Task An_existing_installation_coordinate_prevents_digest_retargeting(string coordinate)
    {
        var setup = await SetupAsync($"existing-{coordinate}");
        var existingRelease = setup.Release.Digest == Digest('f') ? Digest('e') : Digest('f');
        setup.Lifecycle.SeedDifferentReleaseCoordinate(
            coordinate,
            setup.InstallationId,
            existingRelease,
            setup.Grants,
            setup.Subscriptions,
            setup.Context.ActorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.InstallAsync(
            setup.Context,
            new InstallFeatureVersion(
                setup.Draft.DraftId,
                setup.Draft.Revision,
                setup.InstallationId,
                setup.Release.Digest,
                setup.Grants,
                setup.Subscriptions,
                "decision-retargeted",
                "install-retargeted")));

        Assert.Equal(0, setup.Lifecycle.MutationCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
    }

    [Fact]
    public async Task A_partial_install_rejects_an_altered_reviewed_grant_tuple()
    {
        var setup = await SetupAsync("grant-conflict");
        var command = Command(setup);
        setup.Lifecycle.FailAfter = "propose";
        await Assert.ThrowsAsync<IOException>(() => setup.Service.InstallAsync(setup.Context, command));

        await Assert.ThrowsAnyAsync<Exception>(() => setup.Service.InstallAsync(setup.Context, command with
        {
            Grants = [setup.Grants[0] with { ConstraintsJson = "{\"allowedToolIds\":[\"capability.read\"],\"payload\":{\"scope\":[\"write\"]}}" }],
            IdempotencyId = "install-altered-grant"
        }));

        Assert.Equal(1, setup.Lifecycle.ProposeCount);
        Assert.Equal(0, setup.Lifecycle.DecideCount);
    }

    [Theory]
    [InlineData("propose")]
    [InlineData("decide")]
    [InlineData("grant")]
    [InlineData("publish")]
    [InlineData("mark")]
    public async Task Every_partial_install_boundary_resumes_without_duplicate_authority(string boundary)
    {
        var setup = await SetupAsync($"resume-{boundary}");
        var command = Command(setup);
        if (boundary == "mark") setup.Lifecycle.AfterPublication = fixture.Storage.FailNextWrite;
        else setup.Lifecycle.FailAfter = boundary;

        await Assert.ThrowsAnyAsync<Exception>(() => setup.Service.InstallAsync(setup.Context, command));
        Assert.NotNull(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
        var installed = await setup.Service.InstallAsync(setup.Context, command);

        Assert.Equal("installed", installed.Draft.Status);
        Assert.Equal(setup.InstallationId, installed.Draft.InstallationId);
        Assert.Equal(setup.Release.Digest, installed.Release.Digest);
        Assert.Equal(1, setup.Lifecycle.ProposeCount);
        Assert.Equal(1, setup.Lifecycle.DecideCount);
        Assert.Equal(1, setup.Lifecycle.GrantCount);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
        Assert.Empty((await setup.Hub.ReadAsync()).FanOuts);
    }

    [Fact]
    public async Task An_install_reservation_rejects_a_concurrent_Draft_edit_before_lifecycle_mutation()
    {
        var setup = await SetupAsync("edit-race");
        Exception? editFailure = null;
        setup.Lifecycle.BeforePropose = async () =>
        {
            editFailure = await Record.ExceptionAsync(() => setup.Hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
                setup.Draft.DraftId,
                new FeatureBehavior([
                    new FeatureScenario("scenario-install-race", "Race", "installation is reserved", "Behavior is edited", "the edit is rejected")
                ]),
                setup.Draft.Revision,
                "install-race-edit",
                fixture.Time.GetUtcNow().AddMinutes(1))));
        };

        var installed = await setup.Service.InstallAsync(setup.Context, Command(setup));

        Assert.IsType<InvalidOperationException>(editFailure);
        Assert.Equal("installed", installed.Draft.Status);
        Assert.Equal(setup.Release.Digest, installed.Draft.Verification?.Release);
    }

    [Fact]
    public async Task Full_install_replay_only_republishes_without_duplicate_lifecycle_mutation()
    {
        var setup = await SetupAsync("replay");
        var command = Command(setup);

        var first = await setup.Service.InstallAsync(setup.Context, command);
        var replay = await setup.Service.InstallAsync(setup.Context, command);

        Assert.Equal(first.Draft.DraftId, replay.Draft.DraftId);
        Assert.Equal(first.Draft.Revision, replay.Draft.Revision);
        Assert.Equal(1, setup.Lifecycle.ProposeCount);
        Assert.Equal(1, setup.Lifecycle.DecideCount);
        Assert.Equal(1, setup.Lifecycle.GrantCount);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(1, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task A_different_Draft_cannot_claim_an_installed_coordinate_that_the_original_Draft_replays()
    {
        var setup = await SetupAsync("draft-coordinate");
        var command = Command(setup);
        var installed = await setup.Service.InstallAsync(setup.Context, command);
        var replayed = await setup.Service.InstallAsync(setup.Context, command);
        var otherDraft = await setup.Hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-install-other-draft",
            "Install the same release from another Draft",
            fixture.Time.GetUtcNow().AddMinutes(1),
            "conversation-install-other-draft"));
        otherDraft = await setup.Hub.RecordVerificationAsync(new RecordFeatureVerification(
            otherDraft.DraftId,
            new FeatureVerification(setup.Release.Digest, 2, 2, 0, 0, fixture.Time.GetUtcNow().AddMinutes(1)),
            otherDraft.Revision,
            "verify-install-other-draft"));
        var lifecycleMutations = setup.Lifecycle.MutationCount;
        var republications = setup.Lifecycle.RepublishCount;

        await Assert.ThrowsAnyAsync<Exception>(() => setup.Service.InstallAsync(
            setup.Context,
            command with
            {
                DraftId = otherDraft.DraftId,
                ExpectedRevision = otherDraft.Revision,
                IdempotencyId = "install-other-draft"
            }));

        Assert.Equal(installed.Draft.DraftId, replayed.Draft.DraftId);
        Assert.Equal(installed.Draft.Revision, replayed.Draft.Revision);
        Assert.Equal(lifecycleMutations, setup.Lifecycle.MutationCount);
        Assert.Equal(republications, setup.Lifecycle.RepublishCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(otherDraft.DraftId));
        Assert.Equal("draft", (await setup.Hub.ReadDraftAsync(otherDraft.DraftId))?.Status);
    }

    [Fact]
    public async Task A_historical_approval_does_not_block_current_installed_Draft_replay()
    {
        var setup = await SetupAsync("approval-history");
        var command = Command(setup);
        var installed = await setup.Service.InstallAsync(setup.Context, command);
        var historicalRelease = setup.Release.Digest == Digest('f') ? Digest('e') : Digest('f');
        setup.Lifecycle.AddHistoricalApproval(
            setup.InstallationId,
            historicalRelease,
            setup.Grants);

        var replayed = await setup.Service.InstallAsync(setup.Context, command);

        Assert.Equal(installed.Draft.DraftId, replayed.Draft.DraftId);
        Assert.Equal(installed.Draft.Revision, replayed.Draft.Revision);
        Assert.Equal(setup.Release.Digest, replayed.Authority.ActiveRelease);
        Assert.Equal(1, setup.Lifecycle.ProposeCount);
        Assert.Equal(1, setup.Lifecycle.DecideCount);
        Assert.Equal(1, setup.Lifecycle.GrantCount);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(1, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task Unsorted_multi_subscription_install_and_reordered_replay_share_one_canonical_publication()
    {
        var setup = await SetupAsync("subscription-order");
        var command = Command(setup) with { Subscriptions = ["z-event", "a-event"] };

        var installed = await setup.Service.InstallAsync(setup.Context, command);
        var replayed = await setup.Service.InstallAsync(
            setup.Context,
            command with { Subscriptions = ["a-event", "z-event"] });

        Assert.Equal(["a-event", "z-event"], installed.Registration.Subscriptions);
        Assert.Equal(installed.Registration.InstallationId, replayed.Registration.InstallationId);
        Assert.Equal(installed.Registration.Release, replayed.Registration.Release);
        Assert.Equal(installed.Registration.Subscriptions, replayed.Registration.Subscriptions);
        Assert.Equal(1, setup.Lifecycle.ProposeCount);
        Assert.Equal(1, setup.Lifecycle.DecideCount);
        Assert.Equal(1, setup.Lifecycle.GrantCount);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(1, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task Installed_replay_rejects_a_new_idempotency_identity_after_republication()
    {
        var setup = await SetupAsync("replay-identity");
        var command = Command(setup);
        var installed = await setup.Service.InstallAsync(setup.Context, command);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.InstallAsync(
            setup.Context,
            command with { IdempotencyId = "install-different-identity" }));

        var durable = await setup.Hub.ReadDraftAsync(setup.Draft.DraftId);
        Assert.Equal(installed.Draft.UpdatedAt, durable?.UpdatedAt);
        Assert.Equal(installed.Draft.Revision, durable?.Revision);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(1, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task Real_hub_install_registers_the_release_without_enqueuing_or_executing_the_originating_request()
    {
        var setup = await SetupAsync("real-hub-no-execution");
        var lifecycle = new HubLifecycleRail(fixture, setup.Context);
        var service = new FeatureAuthoringService(
            fixture.Cluster.Client,
            new NoBuildEndpoint(),
            setup.Catalog,
            lifecycle,
            fixture.Time);

        var installed = await service.InstallAsync(setup.Context, Command(setup));

        var hub = await setup.Hub.ReadAsync();
        Assert.Contains(hub.Installations, registration =>
            registration.InstallationId == setup.InstallationId && registration.Release == setup.Release.Digest);
        Assert.Empty(hub.FanOuts);
        var runtime = await fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(setup.Context.OwnerId, setup.InstallationId)).ReadAsync();
        Assert.Equal(setup.Release.Digest, runtime.ActiveRelease);
        Assert.Empty(runtime.Inbox);
        Assert.Empty(runtime.Completions);
        Assert.Equal("installed", installed.Draft.Status);
        Assert.Equal(1, lifecycle.PublicationCount);
    }

    [Fact]
    public async Task Installed_command_replay_refuses_to_downgrade_a_newer_active_release()
    {
        var setup = await SetupAsync("replay-downgrade");
        var command = Command(setup);
        await setup.Service.InstallAsync(setup.Context, command);
        var newerRelease = setup.Release.Digest == Digest('f') ? Digest('e') : Digest('f');
        setup.Lifecycle.SwitchActiveRelease(newerRelease);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.InstallAsync(setup.Context, command));

        Assert.Equal(newerRelease, setup.Lifecycle.ActiveRelease);
        Assert.Equal(1, setup.Lifecycle.GrantCount);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(0, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task A_release_switch_during_republish_cannot_finalize_the_draft_for_another_digest()
    {
        var setup = await SetupAsync("republish-race");
        var command = Command(setup);
        setup.Lifecycle.FailAfter = "publish";
        await Assert.ThrowsAsync<IOException>(() => setup.Service.InstallAsync(setup.Context, command));
        var racedRelease = setup.Release.Digest == Digest('f') ? Digest('e') : Digest('f');
        setup.Lifecycle.SwitchToOnRepublish = racedRelease;

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.InstallAsync(setup.Context, command));

        var draft = await setup.Hub.ReadDraftAsync(setup.Draft.DraftId);
        Assert.Equal("draft", draft?.Status);
        Assert.Equal(racedRelease, setup.Lifecycle.ActiveRelease);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(1, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task Draft_finalization_acknowledgement_loss_reconciles_and_replays_with_the_stored_install_time()
    {
        var setup = await SetupAsync("draft-ack");
        var command = Command(setup);
        setup.Lifecycle.AfterPublication = fixture.Storage.CommitThenFailNextWrite;

        var installed = await setup.Service.InstallAsync(setup.Context, command);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var replay = await setup.Service.InstallAsync(setup.Context, command);

        Assert.Equal("installed", installed.Draft.Status);
        Assert.Equal(installed.Draft.UpdatedAt, replay.Draft.UpdatedAt);
        Assert.Equal(installed.Draft.Revision, replay.Draft.Revision);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
    }

    [Fact]
    public async Task Reservation_acknowledgement_loss_reconciles_before_any_lifecycle_mutation()
    {
        var setup = await SetupAsync("reservation-ack");
        fixture.Storage.CommitThenFailNextWrite();

        var installed = await setup.Service.InstallAsync(setup.Context, Command(setup));

        Assert.Equal("installed", installed.Draft.Status);
        Assert.Equal(1, setup.Lifecycle.ProposeCount);
        Assert.Equal(1, setup.Lifecycle.DecideCount);
        Assert.Equal(1, setup.Lifecycle.GrantCount);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
    }

    [Fact]
    public async Task Cross_Owner_and_retargeted_installations_are_rejected()
    {
        var setup = await SetupAsync("identity");
        var command = Command(setup);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => setup.Service.InstallAsync(Context("owner-install-other"), command));
        await setup.Service.InstallAsync(setup.Context, command);
        await Assert.ThrowsAnyAsync<Exception>(() => setup.Service.InstallAsync(setup.Context, command with
        {
            InstallationId = new FeatureInstallationId("installation-retargeted"),
            IdempotencyId = "install-retargeted"
        }));

        Assert.Equal(1, setup.Lifecycle.InstallCount);
    }

    private async Task<Setup> SetupAsync(string suffix)
    {
        var context = Context($"owner-install-{suffix}");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            $"operation-install-{suffix}",
            "Install a reviewed Feature",
            fixture.Time.GetUtcNow(),
            $"conversation-install-{suffix}"));
        var release = Release(suffix);
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            new FeatureVerification(release.Digest, 2, 2, 0, 0, fixture.Time.GetUtcNow()),
            draft.Revision,
            $"verify-install-{suffix}"));
        var grants = new[]
        {
            new FeatureGrantSpec(
                "capability.read",
                1,
                new ProviderConnectionId("connection-reviewed"),
                "{\"allowedToolIds\":[\"capability.read\"]}",
                "google")
        };
        var subscriptions = new[] { "conversation.completed" };
        var lifecycle = new RecordingLifecycleRail(release, fixture.Time.GetUtcNow(), fixture, context, hub);
        var catalog = new RecordingArtifactCatalog(release);
        var service = new FeatureAuthoringService(fixture.Cluster.Client, new NoBuildEndpoint(), catalog, lifecycle, fixture.Time);
        return new Setup(
            context,
            hub,
            draft,
            release,
            new FeatureInstallationId($"installation-{suffix}"),
            grants,
            subscriptions,
            lifecycle,
            catalog,
            service);
    }

    private static InstallFeatureVersion Command(Setup setup) => new(
        setup.Draft.DraftId,
        setup.Draft.Revision,
        setup.InstallationId,
        setup.Release.Digest,
        setup.Grants,
        setup.Subscriptions,
        "decision-reviewed",
        "install-reviewed");

    private static RuntimeRequestContext Context(string owner) => new(
        new BrainOwnerId(owner),
        new ActorId("actor-feature-author"),
        new SessionId("session-feature-author"),
        AuthAssurance.Oidc,
        "correlation-feature-author",
        null,
        new HashSet<string>(["feature.manage"], StringComparer.Ordinal),
        "conversation-feature-author");

    private static FeatureReleaseMetadata Release(string suffix)
    {
        var marker = suffix.Aggregate(0, (value, character) => (value + character) % 16).ToString("x");
        var digest = Digest(marker[0]);
        return new FeatureReleaseMetadata(
            digest,
            $"sha256:{digest.Value}",
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            []);
    }

    private static ReleaseDigest Digest(char marker) => new(new string('0', 63) + marker);

    private sealed record Setup(
        RuntimeRequestContext Context,
        IFeatureHubGrain Hub,
        FeatureDraft Draft,
        FeatureReleaseMetadata Release,
        FeatureInstallationId InstallationId,
        FeatureGrantSpec[] Grants,
        string[] Subscriptions,
        RecordingLifecycleRail Lifecycle,
        RecordingArtifactCatalog Catalog,
        FeatureAuthoringService Service);

    private sealed class NoBuildEndpoint : FeatureBuildEndpoint
    {
        public Task<FeatureBuildArtifact> BuildAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingArtifactCatalog(FeatureReleaseMetadata release) : FeatureArtifactCatalog
    {
        public int CallCount { get; private set; }

        public Task<FeatureReleaseMetadata> DemandReleaseAsync(ReleaseDigest digest, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(release);
        }
    }

    private sealed class RecordingLifecycleRail(
        FeatureReleaseMetadata release,
        DateTimeOffset now,
        FeatureGrainClusterFixture fixture,
        RuntimeRequestContext ownerContext,
        IFeatureHubGrain hub) : FeatureLifecycleRail
    {
        private readonly List<FeatureApprovalSnapshot> _historicalApprovals = [];
        private FeatureApprovalSnapshot? _approval;
        private FeatureAuthoritySnapshot? _authority;
        private FeatureInstallationRegistration? _registration;
        private long _revision;

        public string? FailAfter { get; set; }
        public int ProposeCount { get; private set; }
        public int DecideCount { get; private set; }
        public int GrantCount { get; private set; }
        public int InstallCount { get; private set; }
        public int RepublishCount { get; private set; }
        public ReleaseDigest? ActiveRelease => _authority?.ActiveRelease;
        public ReleaseDigest? SwitchToOnRepublish { get; set; }
        public Func<Task>? BeforePropose { get; set; }
        public Action? AfterPublication { get; set; }
        public FeatureInstallationRegistration? RegistrationOnly { get; set; }
        public int MutationCount => ProposeCount + DecideCount + GrantCount + InstallCount;

        public void SeedDifferentReleaseCoordinate(
            string coordinate,
            FeatureInstallationId installationId,
            ReleaseDigest existingRelease,
            FeatureGrantSpec[] grants,
            string[] subscriptions,
            ActorId actorId)
        {
            var metadata = release with
            {
                Digest = existingRelease,
                SourceReference = $"sha256:{existingRelease.Value}"
            };
            switch (coordinate)
            {
                case "approval":
                    _approval = new FeatureApprovalSnapshot(
                        "approval-existing",
                        installationId,
                        metadata,
                        metadata.RequestedCapabilities,
                        metadata.Dependencies,
                        FeatureApprovalStatus.Approved,
                        "decision-existing",
                        now,
                        1,
                        grants);
                    break;
                case "registration":
                    RegistrationOnly = new FeatureInstallationRegistration(installationId, existingRelease, subscriptions);
                    break;
                case "active-authority":
                    _authority = new FeatureAuthoritySnapshot(
                        installationId,
                        actorId,
                        existingRelease,
                        null,
                        new GrantRevision(1),
                        grants,
                        null,
                        null,
                        [],
                        false,
                        null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(coordinate));
            }
        }

        public void AddHistoricalApproval(
            FeatureInstallationId installationId,
            ReleaseDigest historicalRelease,
            FeatureGrantSpec[] grants)
        {
            var metadata = release with
            {
                Digest = historicalRelease,
                SourceReference = $"sha256:{historicalRelease.Value}"
            };
            _historicalApprovals.Add(new FeatureApprovalSnapshot(
                "approval-historical",
                installationId,
                metadata,
                metadata.RequestedCapabilities,
                metadata.Dependencies,
                FeatureApprovalStatus.Approved,
                "decision-historical",
                now.AddMinutes(-1),
                0,
                grants));
        }

        public Task<FeatureLifecycleInspection> InspectAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default)
        {
            var runtime = _authority?.ActiveRelease is { } activeRelease && _registration is not null
                ? new FeatureInstallationSnapshot(
                    _registration.InstallationId,
                    activeRelease,
                    null,
                    "{}",
                    false,
                    null,
                    [],
                    null,
                    [],
                    [],
                    [],
                    _revision,
                    [])
                : null;
            var installations = _authority is null
                ? Array.Empty<FeatureInstallationInspection>()
                : [new FeatureInstallationInspection(_authority, _registration, runtime)];
            var registrations = new[] { _registration, RegistrationOnly }
                .Where(candidate => candidate is not null)
                .Cast<FeatureInstallationRegistration>()
                .ToArray();
            return Task.FromResult(new FeatureLifecycleInspection(
                _revision,
                _approval is null ? Array.Empty<FeatureReleaseMetadata>() : [release],
                _approval is null ? _historicalApprovals.ToArray() : [.. _historicalApprovals, _approval],
                installations,
                registrations));
        }

        public async Task<FeatureApprovalSnapshot> ProposeAsync(RuntimeRequestContext context, FeatureReleaseProposal proposal, long expectedRevision, CancellationToken cancellationToken = default)
        {
            var callback = BeforePropose;
            BeforePropose = null;
            if (callback is not null) await callback();
            Assert.Equal(_revision, expectedRevision);
            ProposeCount++;
            _revision++;
            _approval = new FeatureApprovalSnapshot(
                "approval-reviewed",
                proposal.InstallationId,
                proposal.Release,
                proposal.Release.RequestedCapabilities,
                [],
                FeatureApprovalStatus.Pending,
                null,
                null,
                _revision,
                proposal.Grants);
            var durable = await hub.ReadAsync().WaitAsync(cancellationToken);
            await hub.ProposeAsync(proposal, durable.Revision).WaitAsync(cancellationToken);
            Fail("propose");
            return _approval;
        }

        public async Task<FeatureApprovalSnapshot> DecideAsync(RuntimeRequestContext context, FeatureApprovalDecision decision, long expectedRevision, CancellationToken cancellationToken = default)
        {
            Assert.Equal(_revision, expectedRevision);
            DecideCount++;
            _revision++;
            _approval = Assert.IsType<FeatureApprovalSnapshot>(_approval) with
            {
                Status = FeatureApprovalStatus.Approved,
                DecisionId = decision.DecisionId,
                DecidedAt = now,
                Revision = _revision
            };
            var durable = await hub.ReadAsync().WaitAsync(cancellationToken);
            var durableApproval = durable.Approvals.Single(candidate =>
                candidate.InstallationId == _approval.InstallationId && candidate.Release.Digest == decision.Release);
            await hub.DecideAsync(
                decision with { ApprovalId = durableApproval.ApprovalId },
                durable.Revision).WaitAsync(cancellationToken);
            Fail("decide");
            return _approval;
        }

        public async Task<FeatureAuthoritySnapshot> GrantAsync(RuntimeRequestContext context, FeatureInstallationId installationId, ReleaseDigest releaseDigest, FeatureGrantSpec[] grants, long expectedRevision, CancellationToken cancellationToken = default)
        {
            Assert.Equal(_revision, expectedRevision);
            GrantCount++;
            _revision++;
            _authority = new FeatureAuthoritySnapshot(
                installationId,
                context.ActorId,
                null,
                null,
                null,
                [],
                releaseDigest,
                new GrantRevision(_revision),
                grants,
                false,
                null);
            var durable = await hub.ReadAsync().WaitAsync(cancellationToken);
            await hub.GrantAsync(
                new FeatureGrantRequest(installationId, releaseDigest, context.ActorId, grants),
                durable.Revision).WaitAsync(cancellationToken);
            Fail("grant");
            return _authority;
        }

        public async Task<FeatureAuthoritySnapshot> InstallAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, long expectedRevision, CancellationToken cancellationToken = default)
        {
            Assert.Equal(_revision, expectedRevision);
            InstallCount++;
            _revision++;
            var authority = Assert.IsType<FeatureAuthoritySnapshot>(_authority);
            _authority = authority with
            {
                ActiveRelease = authority.PendingRelease,
                ActiveGrantRevision = authority.PendingGrantRevision,
                ActiveGrants = authority.PendingGrants,
                PendingRelease = null,
                PendingGrantRevision = null,
                PendingGrants = []
            };
            _registration = registration;
            var durable = await hub.ReadAsync().WaitAsync(cancellationToken);
            await hub.InstallAsync(registration, durable.Revision).WaitAsync(cancellationToken);
            await fixture.PublishActiveAsync(ownerContext.OwnerId, hub, registration.InstallationId);
            Fail("publish");
            var callback = AfterPublication;
            AfterPublication = null;
            callback?.Invoke();
            return _authority;
        }

        public async Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, CancellationToken cancellationToken = default)
        {
            RepublishCount++;
            Assert.Equal(registration.InstallationId, _authority?.InstallationId);
            if (SwitchToOnRepublish is { } releaseDigest)
            {
                SwitchToOnRepublish = null;
                SwitchActiveRelease(releaseDigest);
            }
            else
            {
                await fixture.PublishActiveAsync(ownerContext.OwnerId, hub, registration.InstallationId);
            }
            return Assert.IsType<FeatureAuthoritySnapshot>(_authority);
        }

        public void SwitchActiveRelease(ReleaseDigest releaseDigest)
        {
            var authority = Assert.IsType<FeatureAuthoritySnapshot>(_authority);
            var registration = Assert.IsType<FeatureInstallationRegistration>(_registration);
            _revision++;
            _authority = authority with
            {
                ActiveRelease = releaseDigest,
                PendingRelease = null,
                PendingGrantRevision = null,
                PendingGrants = []
            };
            _registration = registration with { Release = releaseDigest };
        }

        private void Fail(string boundary)
        {
            if (!string.Equals(FailAfter, boundary, StringComparison.Ordinal)) return;
            FailAfter = null;
            throw new IOException($"Injected failure after {boundary}.");
        }
    }

    private sealed class HubLifecycleRail(FeatureGrainClusterFixture fixture, RuntimeRequestContext context) : FeatureLifecycleRail
    {
        public int PublicationCount { get; private set; }

        public async Task<FeatureLifecycleInspection> InspectAsync(RuntimeRequestContext request, CancellationToken cancellationToken = default)
        {
            var hub = await Hub.ReadAsync().WaitAsync(cancellationToken);
            var installations = new List<FeatureInstallationInspection>();
            foreach (var authority in hub.Authorities)
            {
                var registration = hub.Installations.SingleOrDefault(candidate => candidate.InstallationId == authority.InstallationId);
                var runtime = registration is null
                    ? null
                    : await fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(context.OwnerId, authority.InstallationId))
                        .ReadAsync()
                        .WaitAsync(cancellationToken);
                installations.Add(new FeatureInstallationInspection(authority, registration, runtime));
            }
            return new FeatureLifecycleInspection(hub.Revision, hub.Releases, hub.Approvals, installations, hub.Installations);
        }

        public Task<FeatureApprovalSnapshot> ProposeAsync(RuntimeRequestContext request, FeatureReleaseProposal proposal, long expectedRevision, CancellationToken cancellationToken = default) =>
            Hub.ProposeAsync(proposal, expectedRevision).WaitAsync(cancellationToken);

        public Task<FeatureApprovalSnapshot> DecideAsync(RuntimeRequestContext request, FeatureApprovalDecision decision, long expectedRevision, CancellationToken cancellationToken = default) =>
            Hub.DecideAsync(decision, expectedRevision).WaitAsync(cancellationToken);

        public Task<FeatureAuthoritySnapshot> GrantAsync(RuntimeRequestContext request, FeatureInstallationId installationId, ReleaseDigest release, FeatureGrantSpec[] grants, long expectedRevision, CancellationToken cancellationToken = default) =>
            Hub.GrantAsync(new FeatureGrantRequest(installationId, release, request.ActorId, grants), expectedRevision).WaitAsync(cancellationToken);

        public async Task<FeatureAuthoritySnapshot> InstallAsync(RuntimeRequestContext request, FeatureInstallationRegistration registration, long expectedRevision, CancellationToken cancellationToken = default)
        {
            var authority = await Hub.InstallAsync(registration, expectedRevision).WaitAsync(cancellationToken);
            await fixture.PublishActiveAsync(context.OwnerId, Hub, registration.InstallationId);
            PublicationCount++;
            return authority;
        }

        public async Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext request, FeatureInstallationRegistration registration, CancellationToken cancellationToken = default)
        {
            var snapshot = await Hub.ReadAsync().WaitAsync(cancellationToken);
            var authority = snapshot.Authorities.Single(candidate =>
                candidate.InstallationId == registration.InstallationId && candidate.ActiveRelease == registration.Release);
            var durable = snapshot.Installations.Single(candidate => candidate.InstallationId == registration.InstallationId);
            Assert.Equal(registration.Release, durable.Release);
            Assert.Equal(registration.Subscriptions, durable.Subscriptions);
            await fixture.PublishActiveAsync(context.OwnerId, Hub, registration.InstallationId);
            PublicationCount++;
            return authority;
        }

        private IFeatureHubGrain Hub => fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
    }
}
