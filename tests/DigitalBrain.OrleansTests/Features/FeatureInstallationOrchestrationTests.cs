extern alias McpProject;

using Azure;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.OrleansTests.Capabilities;
using FeatureArtifactCatalog = McpProject::DigitalBrain.Mcp.IFeatureArtifactCatalog;
using FeatureAuthoringService = McpProject::DigitalBrain.Mcp.FeatureAuthoringService;
using FeatureBuildArtifact = McpProject::DigitalBrain.Mcp.FeatureBuildArtifact;
using FeatureBuildEndpoint = McpProject::DigitalBrain.Mcp.IFeatureBuildEndpoint;
using FeatureBuildSubmission = McpProject::DigitalBrain.Mcp.FeatureBuildSubmission;
using FeatureInstallationInspection = McpProject::DigitalBrain.Mcp.FeatureInstallationInspection;
using FeatureInstallationRecoverySnapshot = McpProject::DigitalBrain.Mcp.FeatureInstallationRecoverySnapshot;
using FeatureCapabilityCatalog = McpProject::DigitalBrain.Mcp.IFeatureCapabilityCatalog;
using FeatureLifecycleInspection = McpProject::DigitalBrain.Mcp.FeatureLifecycleInspection;
using FeatureLifecycleRail = McpProject::DigitalBrain.Mcp.IFeatureLifecycleRail;
using ConcreteFeatureLifecycleRail = McpProject::DigitalBrain.Mcp.FeatureLifecycleRail;
using FeatureHubState = DigitalBrain.Kernel.Features.FeatureHubState;
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
    public async Task Empty_access_review_derives_the_exact_catalog_authority_and_manual_trigger()
    {
        var setup = await SetupAsync("server-authored-review");
        var capabilities = new StaticFeatureCapabilityCatalog([
            CapabilityCatalogProjectionTests.Descriptor(grants: ["GmailTools.Send"])
        ]);
        var service = new FeatureAuthoringService(
            fixture.Cluster.Client,
            new NoBuildEndpoint(),
            setup.Catalog,
            setup.Lifecycle,
            fixture.Time,
            capabilities);

        var review = await service.PrepareAccessReviewAsync(
            setup.Context,
            new PrepareFeatureAccessReview(
                setup.Draft.DraftId,
                setup.Draft.Revision,
                setup.InstallationId,
                setup.Release.Digest,
                [],
                []));

        var grant = Assert.Single(review.Grants);
        Assert.Equal("capability.read", grant.CapabilityId);
        Assert.Equal(7, grant.CapabilityVersion);
        Assert.Equal("google", grant.Provider);
        Assert.Equal(new ProviderConnectionId("google"), grant.ProviderConnectionId);
        Assert.Equal(
            "{\"allowedToolIds\":[\"capability.read\",\"GmailTools.Send\"]}",
            grant.ConstraintsJson);
        Assert.Equal(["manual"], review.Subscriptions);
        Assert.Equal(0, setup.Lifecycle.MutationCount);
    }

    [Fact]
    public async Task Empty_access_review_fails_closed_for_unknown_unavailable_or_ambiguous_capabilities()
    {
        var catalogs = new FeatureCapabilityCatalog[]
        {
            new StaticFeatureCapabilityCatalog([]),
            new StaticFeatureCapabilityCatalog([
                CapabilityCatalogProjectionTests.Descriptor(available: false)
            ]),
            new StaticFeatureCapabilityCatalog([
                CapabilityCatalogProjectionTests.Descriptor(
                    connections: ["google-primary", "google-secondary"])
            ]),
            new StaticFeatureCapabilityCatalog([
                CapabilityCatalogProjectionTests.Descriptor(
                    grants: ["GmailTools.Send", "GmailTools.Send"])
            ]),
            new StaticFeatureCapabilityCatalog([
                CapabilityCatalogProjectionTests.Descriptor(
                    grants: Enumerable.Range(0, 33).Select(value => $"Tool{value}.Send").ToArray())
            ])
        };

        for (var index = 0; index < catalogs.Length; index++)
        {
            var setup = await SetupAsync($"server-authored-rejected-{index}");
            var service = new FeatureAuthoringService(
                fixture.Cluster.Client,
                new NoBuildEndpoint(),
                setup.Catalog,
                setup.Lifecycle,
                fixture.Time,
                catalogs[index]);

            var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
                service.PrepareAccessReviewAsync(
                    setup.Context,
                    new PrepareFeatureAccessReview(
                        setup.Draft.DraftId,
                        setup.Draft.Revision,
                        setup.InstallationId,
                        setup.Release.Digest,
                        [],
                        [])));

            Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
            Assert.Equal(0, setup.Lifecycle.MutationCount);
        }
    }

    [Fact]
    public async Task Install_recomputes_catalog_authority_and_rejects_mutated_tools_or_triggers_before_reservation()
    {
        var setup = await SetupAsync("install-canonical-authority");
        var review = await setup.Service.PrepareAccessReviewAsync(
            setup.Context,
            new PrepareFeatureAccessReview(
                setup.Draft.DraftId,
                setup.Draft.Revision,
                setup.InstallationId,
                setup.Release.Digest,
                [],
                []));
        var canonical = new InstallFeatureVersion(
            setup.Draft.DraftId,
            setup.Draft.Revision,
            setup.InstallationId,
            setup.Release.Digest,
            review.Grants,
            review.Subscriptions,
            "decision-canonical-authority",
            "install-canonical-authority");
        InstallFeatureVersion[] tampered =
        [
            canonical with
            {
                Grants =
                [
                    review.Grants[0] with
                    {
                        ConstraintsJson =
                            "{\"allowedToolIds\":[\"capability.read\",\"GmailTools.Send\"]}"
                    }
                ],
                IdempotencyId = "install-expanded-tool"
            },
            canonical with
            {
                Subscriptions = [.. review.Subscriptions, "schedule:weekday"],
                IdempotencyId = "install-expanded-trigger"
            }
        ];

        foreach (var command in tampered)
        {
            var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
                setup.Service.InstallAsync(setup.Context, command));
            Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        }

        Assert.Equal(0, setup.Lifecycle.MutationCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Access_review_and_install_bind_same_digest_metadata_to_verified_source(
        bool install)
    {
        var setup = await SetupAsync($"source-coordinate-{install}");
        setup.Catalog.PublishedSourceReference = $"sha256:{new string('e', 64)}";

        var rejected = install
            ? await Assert.ThrowsAsync<InvalidDataException>(() => setup.Service.InstallAsync(
                setup.Context,
                Command(setup)))
            : await Assert.ThrowsAsync<InvalidDataException>(() => setup.Service.PrepareAccessReviewAsync(
                setup.Context,
                new PrepareFeatureAccessReview(
                    setup.Draft.DraftId,
                    setup.Draft.Revision,
                    setup.InstallationId,
                    setup.Release.Digest,
                    [],
                    [])));

        Assert.Contains("source", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, setup.Catalog.SourceCallCount);
        Assert.Equal(0, setup.Lifecycle.MutationCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
    }

    [Fact]
    public async Task Azure_artifact_and_lifecycle_failures_are_typed_unavailable_rejections()
    {
        var artifactSetup = await SetupAsync("azure-artifact-unavailable");
        artifactSetup.Catalog.Failure = new RequestFailedException(503, "azure-artifact-canary");
        var artifactUnavailable = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            artifactSetup.Service.PrepareAccessReviewAsync(
                artifactSetup.Context,
                new PrepareFeatureAccessReview(
                    artifactSetup.Draft.DraftId,
                    artifactSetup.Draft.Revision,
                    artifactSetup.InstallationId,
                    artifactSetup.Release.Digest,
                    artifactSetup.Grants,
                    artifactSetup.Subscriptions)));

        var lifecycleSetup = await SetupAsync("azure-lifecycle-unavailable");
        lifecycleSetup.Lifecycle.Failure = new RequestFailedException(503, "azure-lifecycle-canary");
        var lifecycleUnavailable = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            lifecycleSetup.Service.PrepareAccessReviewAsync(
                lifecycleSetup.Context,
                new PrepareFeatureAccessReview(
                    lifecycleSetup.Draft.DraftId,
                    lifecycleSetup.Draft.Revision,
                    lifecycleSetup.InstallationId,
                    lifecycleSetup.Release.Digest,
                    lifecycleSetup.Grants,
                    lifecycleSetup.Subscriptions)));

        Assert.Equal(FeatureCommandRejectionReason.Unavailable, artifactUnavailable.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Unavailable, lifecycleUnavailable.Reason);
        Assert.Equal(0, artifactSetup.Lifecycle.MutationCount);
        Assert.Equal(0, lifecycleSetup.Lifecycle.MutationCount);
    }

    [Theory]
    [InlineData(FeatureCommandRejectionReason.Conflict)]
    [InlineData(FeatureCommandRejectionReason.Precondition)]
    [InlineData(FeatureCommandRejectionReason.Unavailable)]
    public async Task Typed_publication_outcomes_cross_the_application_lifecycle_boundary_unchanged(
        FeatureCommandRejectionReason reason)
    {
        var setup = await SetupAsync($"publication-reason-{reason}");
        setup.Lifecycle.Failure = new FeatureCommandRejectedException(reason);

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.PrepareAccessReviewAsync(
                setup.Context,
                new PrepareFeatureAccessReview(
                    setup.Draft.DraftId,
                    setup.Draft.Revision,
                    setup.InstallationId,
                    setup.Release.Digest,
                    setup.Grants,
                    setup.Subscriptions)));

        Assert.Equal(reason, rejected.Reason);
        Assert.Equal(0, setup.Lifecycle.MutationCount);
    }

    [Fact]
    public async Task Digest_and_complete_grant_mismatches_are_rejected_before_lifecycle_mutation()
    {
        var setup = await SetupAsync("mismatch");
        var otherDigest = Digest('b');

        var digestMismatch = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => setup.Service.PrepareAccessReviewAsync(setup.Context, new PrepareFeatureAccessReview(
            setup.Draft.DraftId,
            setup.Draft.Revision,
            setup.InstallationId,
            otherDigest,
            setup.Grants,
            setup.Subscriptions)));
        Assert.Equal(FeatureCommandRejectionReason.Precondition, digestMismatch.Reason);
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

        var retargeted = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => setup.Service.PrepareAccessReviewAsync(setup.Context, new PrepareFeatureAccessReview(
            setup.Draft.DraftId,
            setup.Draft.Revision,
            setup.InstallationId,
            setup.Release.Digest,
            setup.Grants,
            setup.Subscriptions)));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, retargeted.Reason);
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

        var retargeted = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => setup.Service.InstallAsync(
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

        Assert.Equal(FeatureCommandRejectionReason.Precondition, retargeted.Reason);
        Assert.Equal(0, setup.Lifecycle.MutationCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
    }

    [Fact]
    public async Task A_partial_install_rejects_an_altered_reviewed_grant_tuple()
    {
        var setup = await SetupAsync("grant-conflict");
        var command = Command(setup);
        setup.Lifecycle.FailAfter = "propose";
        var unavailable = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.InstallAsync(setup.Context, command));
        Assert.Equal(FeatureCommandRejectionReason.Unavailable, unavailable.Reason);

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
    public async Task Draft_recovery_is_absent_without_a_durable_installation()
    {
        var setup = await SetupAsync("recovery-absent");

        var read = await setup.Service.ReadWithRecoveryAsync(setup.Context, setup.Draft.DraftId);

        Assert.Equal(setup.Draft.DraftId, read.Draft.DraftId);
        Assert.Equal(setup.Draft.Revision, read.Draft.Revision);
        Assert.Equal(setup.Draft.Status, read.Draft.Status);
        Assert.Null(read.Recovery);
        Assert.Equal(0, setup.Catalog.CallCount);
        Assert.Equal(0, setup.Catalog.SourceCallCount);
    }

    [Fact]
    public async Task Reserved_Draft_recovery_restores_the_exact_plan_evidence_and_retry_identity()
    {
        var setup = await SetupAsync("recovery-reserved");
        setup.Lifecycle.FailAfter = "propose";
        var command = Command(setup);
        await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.InstallAsync(setup.Context, command));

        var read = await setup.Service.ReadWithRecoveryAsync(setup.Context, setup.Draft.DraftId);
        var recovery = Assert.IsType<FeatureInstallationRecoverySnapshot>(read.Recovery);

        Assert.Equal(setup.Draft.DraftId, read.Draft.DraftId);
        AssertVerification(setup.Draft.Verification!, recovery.Verification);
        AssertMetadataRelease(setup.Release, recovery.Release);
        Assert.Equal(setup.InstallationId, recovery.InstallationId);
        Assert.Equal(setup.Grants, recovery.Grants);
        Assert.Equal(setup.Subscriptions, recovery.Subscriptions);
        Assert.Null(recovery.PreviousRelease);
        Assert.Equal(command.DecisionId, recovery.DecisionId);
        Assert.Equal(command.IdempotencyId, recovery.IdempotencyId);
        Assert.False(recovery.Installed);
        Assert.False(recovery.RollbackAvailable);
        Assert.False(recovery.Paused);
        Assert.Null(recovery.PauseReason);
    }

    [Fact]
    public async Task Reserved_Draft_recovery_rejects_another_actor_and_another_owner()
    {
        var setup = await SetupAsync("recovery-identity");
        setup.Lifecycle.FailAfter = "propose";
        await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.InstallAsync(setup.Context, Command(setup)));

        var actorRejected = await Assert.ThrowsAsync<FeatureAuthorityRejectedException>(() =>
            setup.Service.ReadWithRecoveryAsync(
                setup.Context with { ActorId = new ActorId("actor-recovery-other") },
                setup.Draft.DraftId));

        Assert.Equal(FeatureAuthorityRejectionReason.ActorMismatch, actorRejected.Reason);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            setup.Service.ReadWithRecoveryAsync(Context("owner-recovery-other"), setup.Draft.DraftId));
    }

    [Fact]
    public async Task Reserved_Draft_recovery_revalidates_the_current_server_authored_plan()
    {
        var setup = await SetupAsync("recovery-current-plan");
        setup.Lifecycle.FailAfter = "propose";
        await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.InstallAsync(setup.Context, Command(setup)));
        var mutationCount = setup.Lifecycle.MutationCount;
        setup.CapabilityCatalog.Replace([
            CapabilityCatalogProjectionTests.Descriptor(version: 2)
        ]);

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.ReadWithRecoveryAsync(setup.Context, setup.Draft.DraftId));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        Assert.Equal(mutationCount, setup.Lifecycle.MutationCount);
        Assert.NotNull(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Draft.DraftId));
    }

    [Theory]
    [InlineData("legacy-grants")]
    [InlineData("legacy-subscriptions")]
    [InlineData("revision")]
    [InlineData("release")]
    [InlineData("command")]
    [InlineData("access")]
    public async Task Reserved_Draft_recovery_fails_closed_for_legacy_or_corrupt_coordinates(string corruption)
    {
        var setup = await SetupAsync($"recovery-corrupt-{corruption}");
        setup.Lifecycle.FailAfter = "propose";
        await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.InstallAsync(setup.Context, Command(setup)));
        await RewriteReservationAsync(setup, reservation => corruption switch
        {
            "legacy-grants" => reservation with { Grants = null },
            "legacy-subscriptions" => reservation with { Subscriptions = null },
            "revision" => reservation with { DraftRevision = checked(reservation.DraftRevision + 1) },
            "release" => reservation with
            {
                Release = reservation.Release == Digest('f') ? Digest('e') : Digest('f')
            },
            "command" => reservation with { CommandDigest = new string('a', 64) },
            "access" => reservation with { AccessDigest = new string('a', 64) },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        }, corruption);

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.ReadWithRecoveryAsync(setup.Context, setup.Draft.DraftId));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
    }

    [Fact]
    public async Task Installed_Draft_recovery_restores_active_state_without_retry_identity()
    {
        var setup = await SetupAsync("recovery-installed");
        await setup.Service.InstallAsync(setup.Context, Command(setup));
        setup.Lifecycle.PauseActive();

        var read = await setup.Service.ReadWithRecoveryAsync(setup.Context, setup.Draft.DraftId);
        var recovery = Assert.IsType<FeatureInstallationRecoverySnapshot>(read.Recovery);

        Assert.Equal("installed", read.Draft.Status);
        AssertVerification(read.Draft.Verification!, recovery.Verification);
        AssertMetadataRelease(setup.Release, recovery.Release);
        Assert.Equal(setup.InstallationId, recovery.InstallationId);
        Assert.Equal(setup.Grants, recovery.Grants);
        Assert.Equal(setup.Subscriptions, recovery.Subscriptions);
        Assert.Null(recovery.PreviousRelease);
        Assert.Null(recovery.DecisionId);
        Assert.Null(recovery.IdempotencyId);
        Assert.True(recovery.Installed);
        Assert.False(recovery.RollbackAvailable);
        Assert.True(recovery.Paused);
        Assert.Equal("paused for test", recovery.PauseReason);
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

        Assert.Equal(
            FeatureCommandRejectionReason.Precondition,
            Assert.IsType<FeatureCommandRejectedException>(editFailure).Reason);
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
            FeatureVerificationTestData.Passing(
                setup.Release.Digest,
                otherDraft.Source,
                2,
                fixture.Time.GetUtcNow().AddMinutes(1)),
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
    public async Task Server_authored_subscription_install_and_replay_share_one_canonical_publication()
    {
        var setup = await SetupAsync("subscription-order");
        var command = Command(setup);

        var installed = await setup.Service.InstallAsync(setup.Context, command);
        var replayed = await setup.Service.InstallAsync(setup.Context, command);

        Assert.Equal(["manual"], installed.Registration.Subscriptions);
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

        await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => setup.Service.InstallAsync(
            setup.Context,
            command with { IdempotencyId = "install-different-identity" }));

        var durable = await setup.Hub.ReadDraftAsync(setup.Draft.DraftId);
        Assert.Equal(installed.Draft.UpdatedAt, durable?.UpdatedAt);
        Assert.Equal(installed.Draft.Revision, durable?.Revision);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(0, setup.Lifecycle.RepublishCount);
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
            fixture.Time,
            new StaticFeatureCapabilityCatalog([
                CapabilityCatalogProjectionTests.Descriptor(version: 1)
            ]));

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
    public async Task Exact_install_retry_reuses_the_durable_runtime_baseline_after_normal_active_work()
    {
        var setup = await PrepareRealPendingUpdateAsync("retry-runtime-baseline", stagePendingAuthority: false);
        var runtime = fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(setup.Context.OwnerId, setup.InstallationId));
        var input = new FeatureInput(
            "input-retry-runtime-baseline",
            "manual",
            "{}",
            fixture.Time.GetUtcNow(),
            "correlation-retry-runtime-baseline",
            "trace-retry-runtime-baseline");
        Assert.Equal(FeatureAppendStatus.Paused, await runtime.AppendAsync(input));
        setup.Lifecycle.AfterPublication = fixture.Storage.FailNextWrite;

        await Assert.ThrowsAnyAsync<Exception>(() => setup.Service.InstallAsync(setup.Context, setup.Command));

        Assert.Null(await runtime.ReadReservationAsync());
        Assert.Equal(FeatureAppendStatus.Accepted, await runtime.AppendAsync(input));

        var installed = await setup.Service.InstallAsync(setup.Context, setup.Command);

        Assert.Equal("installed", installed.Draft.Status);
        Assert.Equal(setup.CandidateRelease.Digest, installed.Release.Digest);
        Assert.Equal("input-retry-runtime-baseline", Assert.Single((await runtime.ReadAsync()).Inbox).InputId);
    }

    [Fact]
    public async Task Reset_service_retries_publish_failure_and_confirmation_ack_loss_without_exposing_partial_recovery()
    {
        var setup = await PrepareRealPendingUpdateAsync("reset-two-phase", stagePendingAuthority: true);
        setup.Lifecycle.FailBeforeNextRepublish = true;

        var first = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.ResetInstallationReservationAsync(
                setup.Context,
                setup.Command.DraftId,
                "reset-two-phase"));

        Assert.Equal(FeatureCommandRejectionReason.Unavailable, first.Reason);
        Assert.Equal(
            "reset-two-phase",
            (await setup.Hub.ReadDraftInstallationResetAsync(setup.Command.DraftId))?.IdempotencyId);
        var hidden = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.ReadWithRecoveryAsync(setup.Context, setup.Command.DraftId));
        Assert.Equal(FeatureCommandRejectionReason.Precondition, hidden.Reason);

        setup.Lifecycle.FailAfterNextRepublish = true;
        var acknowledgementLoss = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.ResetInstallationReservationAsync(
                setup.Context,
                setup.Command.DraftId,
                "reset-after-client-restart"));

        Assert.Equal(FeatureCommandRejectionReason.Unavailable, acknowledgementLoss.Reason);
        Assert.Equal(
            "reset-two-phase",
            (await setup.Hub.ReadDraftInstallationResetAsync(setup.Command.DraftId))?.IdempotencyId);

        var completed = await setup.Service.ResetInstallationReservationAsync(
            setup.Context,
            setup.Command.DraftId,
            "reset-after-second-client-restart");
        var replay = await setup.Service.ResetInstallationReservationAsync(
            setup.Context,
            setup.Command.DraftId,
            "reset-after-completion-restart");

        Assert.Null(completed.Recovery);
        Assert.Null(completed.Draft.Verification);
        Assert.Equal(completed.Draft.DraftId, replay.Draft.DraftId);
        Assert.Equal(completed.Draft.Revision, replay.Draft.Revision);
        Assert.Equal(completed.Draft.UpdatedAt, replay.Draft.UpdatedAt);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Command.DraftId));
        Assert.Null(await setup.Hub.ReadDraftInstallationResetAsync(setup.Command.DraftId));
        Assert.Equal(3, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task Reset_service_retries_a_final_hub_write_failure_without_rewriting_baseline_work()
    {
        var setup = await PrepareRealPendingUpdateAsync("reset-final-hub-write", stagePendingAuthority: true);
        var runtime = fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(setup.Context.OwnerId, setup.InstallationId));
        setup.Lifecycle.AfterRepublish = () => fixture.Storage.FailNextWriteForState("feature-hub");

        await Assert.ThrowsAsync<OrleansException>(() => setup.Service.ResetInstallationReservationAsync(
            setup.Context,
            setup.Command.DraftId,
            "reset-final-hub-write"));

        Assert.NotNull(await setup.Hub.ReadDraftInstallationResetAsync(setup.Command.DraftId));
        Assert.Null(await runtime.ReadReservationAsync());
        var input = new FeatureInput(
            "input-reset-final-hub-write",
            "manual",
            "{}",
            fixture.Time.GetUtcNow(),
            "correlation-reset-final-hub-write",
            "trace-reset-final-hub-write");
        Assert.Equal(FeatureAppendStatus.Accepted, await runtime.AppendAsync(input));
        var baselineMutation = await runtime.ReadAsync();

        var completed = await setup.Service.ResetInstallationReservationAsync(
            setup.Context,
            setup.Command.DraftId,
            "reset-final-hub-write-retry");
        var after = await runtime.ReadAsync();

        Assert.Null(completed.Draft.Verification);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Command.DraftId));
        Assert.Null(await setup.Hub.ReadDraftInstallationResetAsync(setup.Command.DraftId));
        Assert.Null(await runtime.ReadReservationAsync());
        Assert.Equal(baselineMutation.Revision, after.Revision);
        Assert.Equal(baselineMutation.StateJson, after.StateJson);
        Assert.Equal(baselineMutation.Lease, after.Lease);
        Assert.Equal(baselineMutation.Inbox, after.Inbox);
    }

    [Fact]
    public async Task Oversized_legacy_recovery_entries_do_not_block_an_exact_two_phase_update_reset()
    {
        var setup = await PrepareRealPendingUpdateAsync("reset-oversized-update", stagePendingAuthority: true);
        var reservation = Assert.IsType<FeatureDraftInstallationReservation>(
            await setup.Hub.ReadDraftInstallationReservationAsync(setup.Command.DraftId));
        fixture.Storage.CommitCompetingStateThenFailNextWrite(state =>
        {
            var durable = (FeatureHubState)state;
            var oversized = reservation with
            {
                DraftId = new FeatureDraftId("draft-reset-oversized-legacy"),
                InstallationId = new FeatureInstallationId("installation-reset-oversized-legacy"),
                Grants =
                [
                    new FeatureGrantSpec(
                        "capability.reset-oversized-legacy",
                        1,
                        null,
                        new string('x', DigitalBrain.Kernel.Features.FeatureLimits.DraftInstallationLedgerUtf8Bytes))
                ]
            };
            return durable with
            {
                DraftInstallationReservations = [.. durable.DraftInstallationReservations!, oversized]
            };
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-reset-oversized-seed",
            "Seed oversized recovery state",
            fixture.Time.GetUtcNow(),
            "conversation-reset-oversized-seed")));
        await fixture.Cluster.DeactivateAsync((IAddressable)setup.Hub);

        var completed = await setup.Service.ResetInstallationReservationAsync(
            setup.Context,
            setup.Command.DraftId,
            "reset-oversized-update");

        Assert.Null(completed.Draft.Verification);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(setup.Command.DraftId));
        Assert.Null(await setup.Hub.ReadDraftInstallationResetAsync(setup.Command.DraftId));
        Assert.Equal(1, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task Installed_command_replay_refuses_to_downgrade_a_newer_active_release()
    {
        var setup = await SetupAsync("replay-downgrade");
        var command = Command(setup);
        await setup.Service.InstallAsync(setup.Context, command);
        var newerRelease = setup.Release.Digest == Digest('f') ? Digest('e') : Digest('f');
        setup.Lifecycle.SwitchActiveRelease(newerRelease);

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => setup.Service.InstallAsync(setup.Context, command));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
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
        var unavailable = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.InstallAsync(setup.Context, command));
        Assert.Equal(FeatureCommandRejectionReason.Unavailable, unavailable.Reason);
        var racedRelease = setup.Release.Digest == Digest('f') ? Digest('e') : Digest('f');
        setup.Lifecycle.SwitchToOnRepublish = racedRelease;

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => setup.Service.InstallAsync(setup.Context, command));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        var draft = await setup.Hub.ReadDraftAsync(setup.Draft.DraftId);
        Assert.Equal("draft", draft?.Status);
        Assert.Equal(racedRelease, setup.Lifecycle.ActiveRelease);
        Assert.Equal(1, setup.Lifecycle.InstallCount);
        Assert.Equal(1, setup.Lifecycle.RepublishCount);
    }

    [Fact]
    public async Task Public_republish_rejects_another_actor_without_mutating_the_reservation_hold()
    {
        var setup = await PrepareRealPendingUpdateAsync("public-republish-actor", stagePendingAuthority: false);
        var runtime = fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(setup.Context.OwnerId, setup.InstallationId));
        var beforeHub = await setup.Hub.ReadAsync();
        var beforeHold = Assert.IsType<FeatureRuntimeReservationSnapshot>(await runtime.ReadReservationAsync());
        var lifecycle = new ConcreteFeatureLifecycleRail(fixture.Cluster.Client, null!, null!);

        var rejected = await Assert.ThrowsAsync<FeatureAuthorityRejectedException>(() =>
            lifecycle.RepublishAsync(
                setup.Context with { ActorId = new ActorId("actor-public-republish-other") },
                setup.InstallationId));

        Assert.Equal(FeatureAuthorityRejectionReason.ActorMismatch, rejected.Reason);
        var afterHub = await setup.Hub.ReadAsync();
        var afterHold = Assert.IsType<FeatureRuntimeReservationSnapshot>(await runtime.ReadReservationAsync());
        Assert.Equal(beforeHub.Revision, afterHub.Revision);
        Assert.Equal(beforeHub.Authorities, afterHub.Authorities);
        var beforeRegistration = Assert.Single(beforeHub.Installations);
        var afterRegistration = Assert.Single(afterHub.Installations);
        Assert.Equal(beforeRegistration.InstallationId, afterRegistration.InstallationId);
        Assert.Equal(beforeRegistration.Release, afterRegistration.Release);
        Assert.Equal(beforeRegistration.Subscriptions, afterRegistration.Subscriptions);
        Assert.Equal(beforeHold.Reservation, afterHold.Reservation);
        Assert.Equal(beforeHold.Phase, afterHold.Phase);
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
    public async Task Installed_update_exposes_the_previous_source_and_exact_rollback_restores_it()
    {
        var context = Context("owner-install-governed-update");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var installationId = new FeatureInstallationId("installation-governed-update");
        var sourceA = Source("VersionA");
        var sourceB = Source("VersionB");
        var releaseA = new FeatureReleaseMetadata(
            Digest('a'),
            SourceReference(sourceA),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.first"],
            [],
            sourceA);
        var releaseB = new FeatureReleaseMetadata(
            Digest('b'),
            SourceReference(sourceB),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.second"],
            [],
            sourceB);
        var catalog = new RecordingArtifactCatalog(releaseA, releaseB);
        var publication = new PublicationProbe();
        var lifecycle = new HubLifecycleRail(fixture, context, publication);
        var capabilityCatalog = new StaticFeatureCapabilityCatalog([
            CapabilityCatalogProjectionTests.Descriptor(
                id: "capability.first",
                version: 1,
                connections: []),
            CapabilityCatalogProjectionTests.Descriptor(
                id: "capability.second",
                version: 1,
                connections: [])
        ]);
        var service = new FeatureAuthoringService(
            fixture.Cluster.Client,
            new NoBuildEndpoint(),
            catalog,
            lifecycle,
            fixture.Time,
            capabilityCatalog);
        var draftA = await VerifiedDraftAsync(hub, "governed-update-a", releaseA);
        var reviewA = await service.PrepareAccessReviewAsync(context, new PrepareFeatureAccessReview(
            draftA.DraftId,
            draftA.Revision,
            installationId,
            releaseA.Digest,
            [],
            []));
        await service.InstallAsync(context, new InstallFeatureVersion(
            draftA.DraftId,
            draftA.Revision,
            installationId,
            releaseA.Digest,
            reviewA.Grants,
            reviewA.Subscriptions,
            "decision-governed-a",
            "install-governed-a"));
        var draftB = await VerifiedDraftAsync(hub, "governed-update-b", releaseB);

        var review = await service.PrepareAccessReviewAsync(context, new PrepareFeatureAccessReview(
            draftB.DraftId,
            draftB.Revision,
            installationId,
            releaseB.Digest,
            [],
            []));
        var installedB = await service.InstallAsync(context, new InstallFeatureVersion(
            draftB.DraftId,
            draftB.Revision,
            installationId,
            releaseB.Digest,
            review.Grants,
            review.Subscriptions,
            "decision-governed-b",
            "install-governed-b"));
        var detailB = await service.ReadInstalledAsync(context, draftB.DraftId);
        var recoveredB = Assert.IsType<FeatureInstallationRecoverySnapshot>(
            (await service.ReadWithRecoveryAsync(context, draftB.DraftId)).Recovery);
        var historicalA = await service.ReadWithRecoveryAsync(context, draftA.DraftId);
        var recoveredHistoricalA = Assert.IsType<FeatureInstallationRecoverySnapshot>(historicalA.Recovery);

        Assert.Equal(sourceA, review.PreviousRelease?.Source);
        Assert.Equal(sourceB, review.Candidate.Release.Source);
        Assert.Equal(releaseB.Digest, installedB.Release.Digest);
        Assert.Equal(releaseB.Digest, detailB.ActiveRelease.Digest);
        Assert.Equal(releaseA.Digest, detailB.PreviousRelease?.Digest);
        Assert.Equal("capability.second", Assert.Single(detailB.Authority.ActiveGrants).CapabilityId);
        Assert.Equal(["manual"], detailB.Registration.Subscriptions);
        Assert.True(recoveredB.Installed);
        Assert.True(recoveredB.RollbackAvailable);
        Assert.Equal(releaseB.Digest, recoveredB.Release.Digest);
        Assert.Null(recoveredB.Release.Source);
        Assert.Equal(releaseA.Digest, recoveredB.PreviousRelease?.Digest);
        Assert.Null(recoveredB.PreviousRelease?.Source);
        Assert.Null(recoveredB.DecisionId);
        Assert.Null(recoveredB.IdempotencyId);
        Assert.Equal(draftA.DraftId, historicalA.Draft.DraftId);
        AssertVerification(draftA.Verification!, historicalA.Draft.Verification!);
        AssertVerification(draftB.Verification!, recoveredHistoricalA.Verification);
        Assert.Equal(releaseB.Digest, recoveredHistoricalA.Release.Digest);
        Assert.NotEqual(historicalA.Draft.Verification!.Release, recoveredHistoricalA.Verification.Release);
        Assert.Equal(releaseB.Digest, publication.ActiveRelease);
        Assert.True(Assert.Single((await hub.ReadAsync()).Authorities).PublicationConfirmed);

        var command = new RollbackFeatureVersion(
            draftB.DraftId,
            releaseB.Digest,
            releaseA.Digest,
            "rollback-governed-update",
            detailB.Revision);
        var stale = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            service.RollbackAsync(context, command with
            {
                ExpectedRevision = detailB.Revision - 1,
                IdempotencyId = "rollback-governed-update-stale"
            }));
        lifecycle.FailBeforeNextRollbackPublication = true;
        var publicationFailure = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            service.RollbackAsync(context, command));
        Assert.Equal(releaseB.Digest, publication.ActiveRelease);
        Assert.False(Assert.Single((await hub.ReadAsync()).Authorities).PublicationConfirmed);

        var freshLifecycle = new HubLifecycleRail(fixture, context, publication);
        var freshService = new FeatureAuthoringService(
            fixture.Cluster.Client,
            new NoBuildEndpoint(),
            catalog,
            freshLifecycle,
            fixture.Time,
            capabilityCatalog);
        var recoveredAfterRollback = await freshService.ReadWithRecoveryAsync(context, draftB.DraftId);
        var activeAfterRollback = Assert.IsType<FeatureInstallationRecoverySnapshot>(recoveredAfterRollback.Recovery);
        var rolledBack = await freshService.RollbackAsync(context, command);
        var mismatchedReplay = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            freshService.RollbackAsync(context, command with { ExpectedRevision = rolledBack.Revision }));
        var replayed = await freshService.RollbackAsync(context, command);
        var runtime = await fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(context.OwnerId, installationId)).ReadAsync();

        Assert.Equal(releaseA.Digest, rolledBack.ActiveRelease.Digest);
        Assert.Equal(FeatureCommandRejectionReason.Conflict, stale.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Unavailable, publicationFailure.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Conflict, mismatchedReplay.Reason);
        Assert.Equal(draftB.DraftId, recoveredAfterRollback.Draft.DraftId);
        AssertVerification(draftB.Verification!, recoveredAfterRollback.Draft.Verification!);
        AssertVerification(draftA.Verification!, activeAfterRollback.Verification);
        Assert.Equal(releaseA.Digest, activeAfterRollback.Release.Digest);
        Assert.NotEqual(recoveredAfterRollback.Draft.Verification!.Release, activeAfterRollback.Verification.Release);
        Assert.False(activeAfterRollback.RollbackAvailable);
        Assert.Null(activeAfterRollback.PreviousRelease);
        Assert.Equal(releaseA.Digest, publication.ActiveRelease);
        Assert.True(Assert.Single((await hub.ReadAsync()).Authorities).PublicationConfirmed);
        Assert.Equal(1, freshLifecycle.RepublishCount);
        Assert.Null(rolledBack.PreviousRelease);
        Assert.Equal("capability.first", Assert.Single(rolledBack.Authority.ActiveGrants).CapabilityId);
        Assert.Equal(["manual"], rolledBack.Registration.Subscriptions);
        Assert.Equal(rolledBack.ActiveRelease.Digest, replayed.ActiveRelease.Digest);
        Assert.Equal(releaseA.Digest, runtime.ActiveRelease);
        Assert.Null(runtime.PreviousRelease);
    }

    [Fact]
    public async Task Legacy_previous_release_without_exact_rollback_availability_is_not_hydrated_or_mutated()
    {
        var setup = await SetupAsync("legacy-rollback-unavailable");
        await setup.Service.InstallAsync(setup.Context, Command(setup));
        var legacyPrevious = setup.Release.Digest == Digest('f') ? Digest('e') : Digest('f');
        setup.Lifecycle.SeedLegacyPreviousRelease(legacyPrevious);

        var detail = await setup.Service.ReadInstalledAsync(setup.Context, setup.Draft.DraftId);
        var recovery = Assert.IsType<FeatureInstallationRecoverySnapshot>(
            (await setup.Service.ReadWithRecoveryAsync(setup.Context, setup.Draft.DraftId)).Recovery);
        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.RollbackAsync(
                setup.Context,
                new RollbackFeatureVersion(
                    setup.Draft.DraftId,
                    setup.Release.Digest,
                    legacyPrevious,
                    "rollback-legacy-unavailable",
                    detail.Revision)));

        Assert.False(detail.Authority.ExactRollbackAvailable);
        Assert.Null(detail.PreviousRelease);
        Assert.False(recovery.RollbackAvailable);
        Assert.Null(recovery.PreviousRelease);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, rejected.Reason);
        Assert.Equal(0, setup.Lifecycle.RollbackCallCount);
    }

    [Fact]
    public async Task Server_authored_update_preserves_exact_active_bindings_and_requires_previous_source()
    {
        var context = Context("owner-server-authored-update");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var installationId = new FeatureInstallationId("installation-server-authored-update");
        var releaseA = new FeatureReleaseMetadata(
            Digest('c'),
            SourceReference(Source("ServerAuthoredA")),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read", "capability.removed"],
            [],
            Source("ServerAuthoredA"));
        var releaseB = new FeatureReleaseMetadata(
            Digest('d'),
            SourceReference(Source("ServerAuthoredB")),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read", "capability.added"],
            [],
            Source("ServerAuthoredB"));
        var activeGrants = new[]
        {
            new FeatureGrantSpec(
                "capability.read",
                7,
                new ProviderConnectionId("google-primary"),
                "{\"allowedToolIds\":[\"capability.read\"]}",
                "google-primary"),
            new FeatureGrantSpec(
                "capability.removed",
                3,
                null,
                "{\"allowedToolIds\":[\"capability.removed\"]}")
        };
        var activeSubscriptions = new[] { "manual" };
        var artifacts = new RecordingArtifactCatalog(releaseA, releaseB);
        var lifecycle = new HubLifecycleRail(fixture, context);
        var service = new FeatureAuthoringService(
            fixture.Cluster.Client,
            new NoBuildEndpoint(),
            artifacts,
            lifecycle,
            fixture.Time,
            new StaticFeatureCapabilityCatalog([
                CapabilityCatalogProjectionTests.Descriptor(connections: ["google-primary"]),
                CapabilityCatalogProjectionTests.Descriptor(
                    id: "capability.removed",
                    version: 3,
                    connections: []),
                CapabilityCatalogProjectionTests.Descriptor(
                    id: "capability.added",
                    version: 9,
                    connections: ["salesforce"])
            ]));
        var draftA = await VerifiedDraftAsync(hub, "server-authored-update-a", releaseA);
        await service.InstallAsync(
            context,
            new InstallFeatureVersion(
                draftA.DraftId,
                draftA.Revision,
                installationId,
                releaseA.Digest,
                activeGrants,
                activeSubscriptions,
                "decision-server-authored-a",
                "install-server-authored-a"));
        var draftB = await VerifiedDraftAsync(hub, "server-authored-update-b", releaseB);
        artifacts.MissingSourceReference = releaseA.SourceReference;

        var unavailable = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            service.PrepareAccessReviewAsync(
                context,
                new PrepareFeatureAccessReview(
                    draftB.DraftId,
                    draftB.Revision,
                    installationId,
                    releaseB.Digest,
                    [],
                    [])));
        Assert.Equal(FeatureCommandRejectionReason.Unavailable, unavailable.Reason);

        artifacts.MissingSourceReference = null;
        var review = await service.PrepareAccessReviewAsync(
            context,
            new PrepareFeatureAccessReview(
                draftB.DraftId,
                draftB.Revision,
                installationId,
                releaseB.Digest,
                [],
                []));

        Assert.Equal(2, review.Grants.Length);
        Assert.Equal(activeGrants[0], review.Grants.Single(grant => grant.CapabilityId == "capability.read"));
        var added = review.Grants.Single(grant => grant.CapabilityId == "capability.added");
        Assert.Equal(9, added.CapabilityVersion);
        Assert.Equal("salesforce", added.Provider);
        Assert.Equal(new ProviderConnectionId("salesforce"), added.ProviderConnectionId);
        Assert.Equal("{\"allowedToolIds\":[\"capability.added\"]}", added.ConstraintsJson);
        Assert.DoesNotContain(review.Grants, grant => grant.CapabilityId == "capability.removed");
        Assert.Equal(activeSubscriptions.Order(StringComparer.Ordinal), review.Subscriptions);
        Assert.Equal(releaseA.Digest, review.PreviousRelease?.Digest);
        Assert.Equal(releaseA.Source, review.PreviousRelease?.Source);
    }

    [Fact]
    public async Task Paused_authority_rejects_update_review_and_install_before_new_mutation()
    {
        var setup = await SetupAsync("paused-update");
        await setup.Service.InstallAsync(setup.Context, Command(setup));
        setup.Lifecycle.PauseActive();
        var mutationCount = setup.Lifecycle.MutationCount;
        var release = Release("paused-update-next");
        setup.Catalog.Add(release);
        var draft = await VerifiedDraftAsync(setup.Hub, "paused-update-next", release);

        var reviewRejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.PrepareAccessReviewAsync(
                setup.Context,
                new PrepareFeatureAccessReview(
                    draft.DraftId,
                    draft.Revision,
                    setup.InstallationId,
                    release.Digest,
                    [],
                    [])));
        var installRejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() =>
            setup.Service.InstallAsync(
                setup.Context,
                new InstallFeatureVersion(
                    draft.DraftId,
                    draft.Revision,
                    setup.InstallationId,
                    release.Digest,
                    setup.Grants,
                    setup.Subscriptions,
                    "decision-paused-update",
                    "install-paused-update")));

        Assert.Equal(FeatureCommandRejectionReason.Precondition, reviewRejected.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, installRejected.Reason);
        Assert.Equal(mutationCount, setup.Lifecycle.MutationCount);
        Assert.Null(await setup.Hub.ReadDraftInstallationReservationAsync(draft.DraftId));
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
        draft = await hub.ReviseSourceAsync(new ReviseFeatureSource(
            draft.DraftId,
            release.Source!,
            draft.Revision,
            $"source-install-{suffix}",
            fixture.Time.GetUtcNow()));
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(release.Digest, draft.Source, 2, fixture.Time.GetUtcNow()),
            draft.Revision,
            $"verify-install-{suffix}"));
        var grants = new[]
        {
            new FeatureGrantSpec(
                "capability.read",
                1,
                new ProviderConnectionId("google"),
                "{\"allowedToolIds\":[\"capability.read\"]}",
                "google")
        };
        var subscriptions = new[] { "manual" };
        var lifecycle = new RecordingLifecycleRail(release, fixture.Time.GetUtcNow(), fixture, context, hub);
        var catalog = new RecordingArtifactCatalog(release);
        var capabilityCatalog = new StaticFeatureCapabilityCatalog([
            CapabilityCatalogProjectionTests.Descriptor(version: 1)
        ]);
        var service = new FeatureAuthoringService(
            fixture.Cluster.Client,
            new NoBuildEndpoint(),
            catalog,
            lifecycle,
            fixture.Time,
            capabilityCatalog);
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
            capabilityCatalog,
            service);
    }

    private async Task<RealPendingUpdateSetup> PrepareRealPendingUpdateAsync(string suffix, bool stagePendingAuthority)
    {
        var context = Context("owner-" + suffix);
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var installationId = new FeatureInstallationId("installation-" + suffix);
        var activeSource = Source(suffix + "-active");
        var candidateSource = Source(suffix + "-candidate");
        var activeRelease = new FeatureReleaseMetadata(
            Digest('a'),
            SourceReference(activeSource),
            FeatureSourceKind.RuntimeAuthored,
            [],
            [],
            activeSource);
        var candidateRelease = new FeatureReleaseMetadata(
            Digest('b'),
            SourceReference(candidateSource),
            FeatureSourceKind.RuntimeAuthored,
            [],
            [],
            candidateSource);
        var catalog = new RecordingArtifactCatalog(activeRelease, candidateRelease);
        var lifecycle = new HubLifecycleRail(fixture, context);
        var service = new FeatureAuthoringService(
            fixture.Cluster.Client,
            new NoBuildEndpoint(),
            catalog,
            lifecycle,
            fixture.Time,
            new StaticFeatureCapabilityCatalog([]));
        var activeDraft = await VerifiedDraftAsync(hub, suffix + "-active", activeRelease);
        await service.InstallAsync(context, new InstallFeatureVersion(
            activeDraft.DraftId,
            activeDraft.Revision,
            installationId,
            activeRelease.Digest,
            [],
            ["manual"],
            "decision-" + suffix + "-active",
            "install-" + suffix + "-active"));
        var candidateDraft = await VerifiedDraftAsync(hub, suffix + "-candidate", candidateRelease);
        var command = new InstallFeatureVersion(
            candidateDraft.DraftId,
            candidateDraft.Revision,
            installationId,
            candidateRelease.Digest,
            [],
            ["manual"],
            "decision-" + suffix + "-candidate",
            "install-" + suffix + "-candidate");
        var runtime = await fixture.Grain<IFeatureInstallationGrain>(
            FeatureGrainIds.Installation(context.OwnerId, installationId)).ReadAsync();
        var reservedCommand = command with
        {
            RuntimeRevision = runtime.Revision,
            RuntimeActiveRelease = runtime.ActiveRelease,
            RuntimePreviousRelease = runtime.PreviousRelease
        };
        await hub.AcquireDraftInstallationReservationAsync(reservedCommand, context.ActorId);
        if (stagePendingAuthority)
        {
            var approval = await hub.ProposeAsync(
                new FeatureReleaseProposal(installationId, candidateRelease, []),
                (await hub.ReadAsync()).Revision);
            await hub.DecideAsync(
                new FeatureApprovalDecision(
                    approval.ApprovalId,
                    candidateRelease.Digest,
                    true,
                    command.DecisionId,
                    context.ActorId),
                (await hub.ReadAsync()).Revision);
            await hub.GrantAsync(
                new FeatureGrantRequest(installationId, candidateRelease.Digest, context.ActorId, []),
                (await hub.ReadAsync()).Revision);
        }
        return new RealPendingUpdateSetup(
            context,
            hub,
            service,
            lifecycle,
            installationId,
            candidateRelease,
            command);
    }

    private async Task<FeatureDraft> VerifiedDraftAsync(
        IFeatureHubGrain hub,
        string suffix,
        FeatureReleaseMetadata release)
    {
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            $"operation-{suffix}",
            $"Build {suffix}",
            fixture.Time.GetUtcNow(),
            $"conversation-{suffix}"));
        draft = await hub.ReviseSourceAsync(new ReviseFeatureSource(
            draft.DraftId,
            release.Source ?? throw new InvalidOperationException("A test release source is required."),
            draft.Revision,
            $"source-{suffix}",
            fixture.Time.GetUtcNow()));
        return await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(release.Digest, draft.Source, 1, fixture.Time.GetUtcNow()),
            draft.Revision,
            $"verify-{suffix}"));
    }

    private async Task RewriteReservationAsync(
        Setup setup,
        Func<FeatureDraftInstallationReservation, FeatureDraftInstallationReservation> rewrite,
        string suffix)
    {
        fixture.Storage.CommitCompetingStateThenFailNextWrite(state =>
        {
            var durable = (FeatureHubState)state;
            return durable with
            {
                DraftInstallationReservations = durable.DraftInstallationReservations!
                    .Select(reservation => reservation.DraftId == setup.Draft.DraftId
                        ? rewrite(reservation)
                        : reservation)
                    .ToArray()
            };
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Hub.CreateDraftAsync(new CreateFeatureDraft(
            $"operation-rewrite-{suffix}",
            "Advance durable state",
            fixture.Time.GetUtcNow().AddMinutes(1),
            $"conversation-rewrite-{suffix}")));
        await fixture.Cluster.DeactivateAsync((IAddressable)setup.Hub);
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

    private static void AssertVerification(FeatureVerification expected, FeatureVerification actual)
    {
        Assert.Equal(expected.Release, actual.Release);
        Assert.Equal(expected.Total, actual.Total);
        Assert.Equal(expected.Passed, actual.Passed);
        Assert.Equal(expected.Failed, actual.Failed);
        Assert.Equal(expected.Skipped, actual.Skipped);
        Assert.Equal(expected.VerifiedAt, actual.VerifiedAt);
        var expectedEvidence = Assert.IsType<FeatureVerificationEvidence>(expected.Evidence);
        var actualEvidence = Assert.IsType<FeatureVerificationEvidence>(actual.Evidence);
        Assert.Equal(expectedEvidence.SourceReference, actualEvidence.SourceReference);
        Assert.Equal(expectedEvidence.Total, actualEvidence.Total);
        Assert.Equal(expectedEvidence.Passed, actualEvidence.Passed);
        Assert.Equal(expectedEvidence.Failed, actualEvidence.Failed);
        Assert.Equal(expectedEvidence.Skipped, actualEvidence.Skipped);
        Assert.Equal(expectedEvidence.Scenarios, actualEvidence.Scenarios);
        Assert.Equal(expectedEvidence.Artifacts, actualEvidence.Artifacts);
    }

    private static void AssertMetadataRelease(FeatureReleaseMetadata expected, FeatureReleaseMetadata actual)
    {
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(expected.SourceReference, actual.SourceReference);
        Assert.Equal(expected.SourceKind, actual.SourceKind);
        Assert.Equal(expected.RequestedCapabilities, actual.RequestedCapabilities);
        Assert.Equal(expected.Dependencies, actual.Dependencies);
        Assert.Null(actual.Source);
    }

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
        var source = Source(suffix);
        return new FeatureReleaseMetadata(
            digest,
            SourceReference(source),
            FeatureSourceKind.RuntimeAuthored,
            ["capability.read"],
            [],
            source);
    }

    private static ReleaseDigest Digest(char marker) => new(new string('0', 63) + marker);

    private static FeatureSourceSnapshot Source(string suffix) => new(
        $"src/{suffix}/{suffix}.csproj",
        $"tests/{suffix}.Scenarios/{suffix}.Scenarios.csproj",
        [
            new FeatureSourceFile($"src/{suffix}/{suffix}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile($"src/{suffix}/{suffix}.cs", $"namespace {suffix}; public sealed class Feature;"),
            new FeatureSourceFile($"tests/{suffix}.Scenarios/{suffix}.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
        ]);

    private static string SourceReference(FeatureSourceSnapshot source) =>
        DigitalBrain.Kernel.Features.FeatureDraftAuthoringTransitions.SourceReference(source);

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
        StaticFeatureCapabilityCatalog CapabilityCatalog,
        FeatureAuthoringService Service);

    private sealed record RealPendingUpdateSetup(
        RuntimeRequestContext Context,
        IFeatureHubGrain Hub,
        FeatureAuthoringService Service,
        HubLifecycleRail Lifecycle,
        FeatureInstallationId InstallationId,
        FeatureReleaseMetadata CandidateRelease,
        InstallFeatureVersion Command);

    private sealed class NoBuildEndpoint : FeatureBuildEndpoint
    {
        public Task<FeatureBuildArtifact> BuildAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingArtifactCatalog(params FeatureReleaseMetadata[] releases) : FeatureArtifactCatalog
    {
        private readonly Dictionary<ReleaseDigest, FeatureReleaseMetadata> _releases =
            releases.ToDictionary(release => release.Digest);
        public int CallCount { get; private set; }
        public int SourceCallCount { get; private set; }
        public Exception? Failure { get; set; }
        public string? MissingSourceReference { get; set; }
        public string? PublishedSourceReference { get; set; }

        public void Add(FeatureReleaseMetadata release) => _releases.Add(release.Digest, release);

        public Task<FeatureReleaseMetadata> DemandReleaseAsync(ReleaseDigest digest, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Failure is null
                ? Task.FromResult(_releases[digest] with
                {
                    SourceReference = PublishedSourceReference ?? _releases[digest].SourceReference,
                    Source = null
                })
                : Task.FromException<FeatureReleaseMetadata>(Failure);
        }

        public Task<FeatureSourceSnapshot> DemandSourceAsync(string sourceReference, CancellationToken cancellationToken = default)
        {
            SourceCallCount++;
            if (string.Equals(sourceReference, MissingSourceReference, StringComparison.Ordinal))
                return Task.FromException<FeatureSourceSnapshot>(new KeyNotFoundException("The source was not recorded."));
            return Task.FromResult(_releases.Values.Single(release =>
                string.Equals(release.SourceReference, sourceReference, StringComparison.Ordinal)).Source
                ?? throw new KeyNotFoundException("The source was not recorded."));
        }
    }

    private sealed class StaticFeatureCapabilityCatalog(IEnumerable<CapabilityDescriptor> descriptors) : FeatureCapabilityCatalog
    {
        private CapabilityDescriptor[] _descriptors = descriptors.ToArray();

        public void Replace(IEnumerable<CapabilityDescriptor> descriptors) => _descriptors = descriptors.ToArray();

        public Task<IReadOnlyList<CapabilityDescriptor>> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CapabilityDescriptor>>(_descriptors);
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
        public Exception? Failure { get; set; }
        public int ProposeCount { get; private set; }
        public int DecideCount { get; private set; }
        public int GrantCount { get; private set; }
        public int InstallCount { get; private set; }
        public int RepublishCount { get; private set; }
        public int RollbackCallCount { get; private set; }
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
            if (Failure is not null)
                return Task.FromException<FeatureLifecycleInspection>(Failure);
            var runtime = _authority?.ActiveRelease is { } activeRelease && _registration is not null
                ? new FeatureInstallationSnapshot(
                    _registration.InstallationId,
                    activeRelease,
                    null,
                    "{}",
                    _authority.Paused,
                    _authority.PauseReason,
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
            _authority = Assert.IsType<FeatureAuthoritySnapshot>(_authority) with { PublicationConfirmed = true };
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
                _authority = Assert.IsType<FeatureAuthoritySnapshot>(_authority) with { PublicationConfirmed = true };
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

        public void PauseActive()
        {
            var authority = Assert.IsType<FeatureAuthoritySnapshot>(_authority);
            _authority = authority with { Paused = true, PauseReason = "paused for test" };
        }

        public void SeedLegacyPreviousRelease(ReleaseDigest previousRelease)
        {
            var authority = Assert.IsType<FeatureAuthoritySnapshot>(_authority);
            _authority = authority with
            {
                PreviousRelease = previousRelease,
                ExactRollbackAvailable = false
            };
        }

        public Task<FeatureAuthoritySnapshot> RollbackAsync(
            RuntimeRequestContext context,
            RollbackFeatureInstallation command,
            CancellationToken cancellationToken = default)
        {
            RollbackCallCount++;
            return Task.FromException<FeatureAuthoritySnapshot>(
                new InvalidOperationException("Legacy rollback state must be rejected before lifecycle mutation."));
        }

        private void Fail(string boundary)
        {
            if (!string.Equals(FailAfter, boundary, StringComparison.Ordinal)) return;
            FailAfter = null;
            throw new IOException($"Injected failure after {boundary}.");
        }
    }

    private sealed class PublicationProbe
    {
        public ReleaseDigest? ActiveRelease { get; private set; }

        public void Publish(ReleaseDigest release) => ActiveRelease = release;
    }

    private sealed class HubLifecycleRail(
        FeatureGrainClusterFixture fixture,
        RuntimeRequestContext context,
        PublicationProbe? publication = null) : FeatureLifecycleRail
    {
        public int PublicationCount { get; private set; }
        public int RepublishCount { get; private set; }
        public bool FailBeforeNextRollbackPublication { get; set; }
        public bool FailBeforeNextRepublish { get; set; }
        public bool FailAfterNextRepublish { get; set; }
        public Action? AfterPublication { get; set; }
        public Action? AfterRepublish { get; set; }

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
            publication?.Publish(registration.Release);
            PublicationCount++;
            AfterPublication?.Invoke();
            return authority;
        }

        public async Task<FeatureAuthoritySnapshot> RollbackAsync(
            RuntimeRequestContext request,
            RollbackFeatureInstallation command,
            CancellationToken cancellationToken = default)
        {
            var authority = await Hub.RollbackInstallationExactAsync(command).WaitAsync(cancellationToken);
            if (FailBeforeNextRollbackPublication)
            {
                FailBeforeNextRollbackPublication = false;
                throw new IOException("Injected failure before rollback publication.");
            }
            await fixture.PublishActiveAsync(context.OwnerId, Hub, command.InstallationId);
            publication?.Publish(authority.ActiveRelease ?? throw new InvalidOperationException("Rollback has no active release."));
            PublicationCount++;
            return authority;
        }

        public async Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext request, FeatureInstallationRegistration registration, CancellationToken cancellationToken = default)
        {
            RepublishCount++;
            if (FailBeforeNextRepublish)
            {
                FailBeforeNextRepublish = false;
                throw new IOException("Injected failure before reset republication.");
            }
            var snapshot = await Hub.ReadAsync().WaitAsync(cancellationToken);
            var authority = snapshot.Authorities.Single(candidate =>
                candidate.InstallationId == registration.InstallationId && candidate.ActiveRelease == registration.Release);
            var durable = snapshot.Installations.Single(candidate => candidate.InstallationId == registration.InstallationId);
            Assert.Equal(registration.Release, durable.Release);
            Assert.Equal(registration.Subscriptions, durable.Subscriptions);
            await fixture.PublishActiveAsync(context.OwnerId, Hub, registration.InstallationId);
            publication?.Publish(registration.Release);
            PublicationCount++;
            var callback = AfterRepublish;
            AfterRepublish = null;
            callback?.Invoke();
            if (FailAfterNextRepublish)
            {
                FailAfterNextRepublish = false;
                throw new IOException("Injected acknowledgement loss after reset republication.");
            }
            return authority;
        }

        private IFeatureHubGrain Hub => fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
    }
}
