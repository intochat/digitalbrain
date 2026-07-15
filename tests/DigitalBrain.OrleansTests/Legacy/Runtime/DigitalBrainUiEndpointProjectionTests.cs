extern alias McpProject;

using System.Reflection;
using DigitalBrain.Kernel.Contracts;
using AcceptSuggestedChangeInput = McpProject::DigitalBrain.V2.Ui.Grpc.AcceptSuggestedChangeInput;
using DigitalBrainUiEndpoints = McpProject::DigitalBrain.Mcp.DigitalBrainUiEndpoints;
using FeatureDraftRecoverySnapshot = McpProject::DigitalBrain.Mcp.FeatureDraftRecoverySnapshot;
using FeatureInstallationRecoverySnapshot = McpProject::DigitalBrain.Mcp.FeatureInstallationRecoverySnapshot;
using FeatureVerificationReview = McpProject::DigitalBrain.Mcp.FeatureVerificationReview;
using FeatureAccessReviewReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureAccessReviewReply;
using FeatureDraftReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraftReply;
using FeatureInstallReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureInstallReply;
using FeatureReleaseReviewReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureReleaseReviewReply;
using FeatureReleaseSourceReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureReleaseSourceReply;
using FeatureReply = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureReply;
using GrpcFeatureGrant = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureGrant;
using GrpcFeatureBehavior = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureBehavior;
using GrpcFeatureDraftPatch = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraftPatch;
using GrpcFeatureScenario = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureScenario;
using GrpcFeatureSourceFile = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceFile;
using GrpcFeatureSourceSnapshot = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceSnapshot;
using RejectSuggestedChangeInput = McpProject::DigitalBrain.V2.Ui.Grpc.RejectSuggestedChangeInput;
using ReviewFeatureAccessRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviewFeatureAccessRequest;
using ReviseFeatureBehaviorInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureBehaviorInput;
using ReviseFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureDraftRequest;
using ReviseFeatureSourceInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureSourceInput;

namespace DigitalBrain.Tests.Runtime;

public sealed class DigitalBrainUiEndpointProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Access_review_mapping_accepts_only_the_exact_empty_pair_or_the_exact_nonempty_pair()
    {
        var empty = new ReviewFeatureAccessRequest
        {
            DraftId = "draft-access-map",
            ExpectedRevision = 1,
            InstallationId = "installation-access-map",
            ReleaseDigest = Digest('a').Value
        };

        var mapped = InvokeProjection<PrepareFeatureAccessReview>("MapAccessReview", empty);
        Assert.Empty(mapped.Grants);
        Assert.Empty(mapped.Subscriptions);

        var onlyGrant = empty.Clone();
        onlyGrant.Grants.Add(new GrpcFeatureGrant
        {
            CapabilityId = "capability.read",
            CapabilityVersion = 1,
            ConstraintsJson = "{\"allowedToolIds\":[\"capability.read\"]}"
        });
        var onlySubscription = empty.Clone();
        onlySubscription.Subscriptions.Add("manual");

        AssertProjectionRejected("MapAccessReview", onlyGrant);
        AssertProjectionRejected("MapAccessReview", onlySubscription);
    }

    [Fact]
    public void Draft_projection_binds_the_requested_identity_and_validates_Verification()
    {
        var release = Digest('a');
        var valid = Draft("draft-projection", new FeatureVerification(release, 2, 2, 0, 0, Now));

        AssertProjectionRejected(
            "ProjectDraft",
            new FeatureDraftId("different-draft"),
            new FeatureDraftRecoverySnapshot(valid, null));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            new FeatureDraftRecoverySnapshot(
                Draft("draft-projection", new FeatureVerification(release, 2, 1, 0, 0, Now)),
                null));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            new FeatureDraftRecoverySnapshot(
                Draft("draft-projection", new FeatureVerification(
                    release,
                    1,
                    1,
                    0,
                    0,
                    Now.ToOffset(TimeSpan.FromHours(1)))),
                null));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            new FeatureDraftRecoverySnapshot(DraftState(valid.DraftId, "installed", null, null, 4), null));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            new FeatureDraftRecoverySnapshot(
                DraftState(
                    valid.DraftId,
                    "draft",
                    new FeatureVerification(release, 1, 1, 0, 0, Now),
                    new FeatureInstallationId("unexpected-installation"),
                    4),
                null));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            new FeatureDraftRecoverySnapshot(
                DraftState(
                    valid.DraftId,
                    "installed",
                    new FeatureVerification(release, 2, 1, 1, 0, Now),
                    new FeatureInstallationId("installed-with-failed-verification"),
                    4),
                null));
    }

    [Fact]
    public void Draft_recovery_projection_exposes_full_evidence_and_metadata_only_coordinates()
    {
        var release = Release(Digest('1'));
        var previous = Release(Digest('2'));
        var verification = new FeatureVerification(
            release.Digest,
            1,
            1,
            0,
            0,
            Now,
            Evidence(release));
        var draft = Draft("draft-recovery-projection", verification);
        var installationId = new FeatureInstallationId("installation-recovery-projection");
        var recovery = new FeatureInstallationRecoverySnapshot(
            false,
            verification,
            release,
            installationId,
            [Grant("capability.read")],
            ["manual"],
            previous,
            "decision-recovery-projection",
            "install-recovery-projection",
            false,
            false,
            null);

        var reply = InvokeProjection<FeatureDraftReply>(
            "ProjectDraft",
            draft.DraftId,
            new FeatureDraftRecoverySnapshot(draft, recovery));

        Assert.NotNull(reply.Recovery);
        Assert.False(reply.Recovery.Installed);
        Assert.Equal(release.Digest.Value, reply.Recovery.Release.Digest);
        Assert.Null(reply.Recovery.Release.Source);
        Assert.Equal(previous.Digest.Value, reply.Recovery.PreviousRelease.Digest);
        Assert.Null(reply.Recovery.PreviousRelease.Source);
        Assert.Equal(verification.Evidence!.SourceReference, reply.Recovery.Verification.SourceReference);
        Assert.Equal(verification.VerifiedAt.ToUnixTimeMilliseconds(), reply.Recovery.Verification.VerifiedAtUnixMs);
        Assert.Single(reply.Recovery.Verification.Scenarios);
        Assert.Equal("decision-recovery-projection", reply.Recovery.DecisionId);
        Assert.Equal("install-recovery-projection", reply.Recovery.IdempotencyId);
        Assert.False(reply.Recovery.RollbackAvailable);
        Assert.False(reply.Recovery.Paused);
        Assert.False(reply.Recovery.HasPauseReason);
    }

    [Fact]
    public void Installed_recovery_projection_enforces_retry_rollback_and_pause_exclusivity()
    {
        var release = Release(Digest('3'));
        var previous = Release(Digest('4'));
        var verification = new FeatureVerification(
            release.Digest,
            1,
            1,
            0,
            0,
            Now,
            Evidence(release));
        var installationId = new FeatureInstallationId("installation-installed-recovery");
        var draft = DraftState(
            new FeatureDraftId("draft-installed-recovery"),
            "installed",
            verification,
            installationId,
            5);
        var recovery = new FeatureInstallationRecoverySnapshot(
            true,
            verification,
            release,
            installationId,
            [Grant("capability.read")],
            ["manual"],
            previous,
            null,
            null,
            true,
            false,
            null);

        var reply = InvokeProjection<FeatureDraftReply>(
            "ProjectDraft",
            draft.DraftId,
            new FeatureDraftRecoverySnapshot(draft, recovery));

        Assert.True(reply.Recovery.Installed);
        Assert.True(reply.Recovery.RollbackAvailable);
        Assert.NotNull(reply.Recovery.PreviousRelease);
        Assert.False(reply.Recovery.HasDecisionId);
        Assert.False(reply.Recovery.HasIdempotencyId);
        AssertProjectionRejected(
            "ProjectDraft",
            draft.DraftId,
            new FeatureDraftRecoverySnapshot(draft, recovery with { DecisionId = "unexpected-retry" }));
        AssertProjectionRejected(
            "ProjectDraft",
            draft.DraftId,
            new FeatureDraftRecoverySnapshot(draft, recovery with
            {
                RollbackAvailable = false,
                PreviousRelease = previous
            }));
        AssertProjectionRejected(
            "ProjectDraft",
            draft.DraftId,
            new FeatureDraftRecoverySnapshot(draft, recovery with
            {
                RollbackAvailable = false,
                PreviousRelease = null,
                Paused = true,
                PauseReason = null
            }));
    }

    [Fact]
    public void Installed_recovery_projection_preserves_historical_Draft_verification()
    {
        var historicalRelease = Release(Digest('5'));
        var activeRelease = Release(Digest('6'));
        var historicalVerification = new FeatureVerification(
            historicalRelease.Digest,
            1,
            1,
            0,
            0,
            Now,
            Evidence(historicalRelease));
        var activeVerification = new FeatureVerification(
            activeRelease.Digest,
            1,
            1,
            0,
            0,
            Now.AddMinutes(1),
            Evidence(activeRelease));
        var installationId = new FeatureInstallationId("installation-historical-recovery");
        var draft = DraftState(
            new FeatureDraftId("draft-historical-recovery"),
            "installed",
            historicalVerification,
            installationId,
            5);
        var recovery = new FeatureInstallationRecoverySnapshot(
            true,
            activeVerification,
            activeRelease,
            installationId,
            [Grant("capability.read")],
            ["manual"],
            null,
            null,
            null,
            false,
            false,
            null);

        var reply = InvokeProjection<FeatureDraftReply>(
            "ProjectDraft",
            draft.DraftId,
            new FeatureDraftRecoverySnapshot(draft, recovery));

        Assert.Equal(historicalRelease.Digest.Value, reply.Draft.Verification.ReleaseDigest);
        Assert.Equal(activeRelease.Digest.Value, reply.Recovery.Verification.ReleaseDigest);
        Assert.NotEqual(reply.Draft.Verification.ReleaseDigest, reply.Recovery.Verification.ReleaseDigest);
    }

    [Fact]
    public void Suggestion_projection_binds_Draft_and_base_Revision()
    {
        var command = new SuggestFeatureChange(
            new FeatureDraftId("draft-suggestion-projection"),
            7,
            "Produce a safe patch",
            "suggestion-projection");
        var patch = Patch(command.DraftId, command.ExpectedRevision);

        AssertProjectionRejected("ProjectSuggestion", command, patch with
        {
            DraftId = new FeatureDraftId("different-draft")
        });
        AssertProjectionRejected("ProjectSuggestion", command, patch with { BaseRevision = 8 });
    }

    [Fact]
    public void Revision_projection_binds_each_command_to_its_exact_write_semantics()
    {
        var draftId = new FeatureDraftId("draft-revision-projection");
        const long ExpectedRevision = 4;
        var behaviorInput = RevisionInput(new ReviseFeatureDraftRequest
        {
            DraftId = draftId.Value,
            ExpectedRevision = ExpectedRevision,
            IdempotencyId = "behavior-revision-projection",
            ReviseBehavior = new ReviseFeatureBehaviorInput { Behavior = GrpcBehavior("command") }
        });
        var sourceInput = RevisionInput(new ReviseFeatureDraftRequest
        {
            DraftId = draftId.Value,
            ExpectedRevision = ExpectedRevision,
            IdempotencyId = "source-revision-projection",
            ReviseSource = new ReviseFeatureSourceInput { Source = GrpcSource("command") }
        });
        var acceptInput = RevisionInput(new ReviseFeatureDraftRequest
        {
            DraftId = draftId.Value,
            ExpectedRevision = ExpectedRevision,
            IdempotencyId = "accept-revision-projection",
            AcceptSuggestedChange = new AcceptSuggestedChangeInput
            {
                Patch = new GrpcFeatureDraftPatch
                {
                    PatchId = "patch-revision-projection",
                    DraftId = draftId.Value,
                    BaseRevision = ExpectedRevision,
                    Summary = "Apply the exact Suggested Change",
                    ReplacementBehavior = GrpcBehavior("patch"),
                    ReplacementSource = GrpcSource("patch")
                }
            }
        });
        var rejectInput = RevisionInput(new ReviseFeatureDraftRequest
        {
            DraftId = draftId.Value,
            ExpectedRevision = ExpectedRevision,
            IdempotencyId = "reject-revision-projection",
            RejectSuggestedChange = new RejectSuggestedChangeInput
            {
                PatchId = "patch-revision-projection",
                BaseRevision = ExpectedRevision
            }
        });
        var advanced = DraftState(draftId, "draft", null, null, ExpectedRevision + 1);

        AssertProjectionRejected(
            "ProjectRevision",
            behaviorInput,
            Rewrite(advanced, behavior: DomainBehavior("different")));
        AssertProjectionRejected(
            "ProjectRevision",
            behaviorInput,
            Rewrite(advanced, behavior: DomainBehavior("command"), revision: ExpectedRevision));
        AssertProjectionRejected(
            "ProjectRevision",
            sourceInput,
            Rewrite(advanced, source: Source("different")));
        AssertProjectionRejected(
            "ProjectRevision",
            acceptInput,
            Rewrite(advanced, behavior: DomainBehavior("different"), source: Source("patch")));
        AssertProjectionRejected(
            "ProjectRevision",
            acceptInput,
            Rewrite(advanced, behavior: DomainBehavior("patch"), source: Source("different")));
        AssertProjectionRejected("ProjectRevision", rejectInput, advanced);
        var installedVerification = new FeatureVerification(Digest('e'), 1, 1, 0, 0, Now);
        AssertProjectionRejected(
            "ProjectRevision",
            behaviorInput,
            Rewrite(
                DraftState(
                    draftId,
                    "installed",
                    installedVerification,
                    new FeatureInstallationId("installation-revision-projection"),
                    ExpectedRevision + 1),
                behavior: DomainBehavior("command")));
        AssertProjectionRejected(
            "ProjectRevision",
            rejectInput,
            DraftState(
                draftId,
                "installed",
                installedVerification,
                new FeatureInstallationId("installation-revision-reject-projection"),
                ExpectedRevision));
    }

    [Fact]
    public void Verification_projection_binds_Draft_release_and_bounded_release_lists()
    {
        var digest = Digest('b');
        var command = new VerifyFeatureDraft(
            new FeatureDraftId("draft-verification-projection"),
            3,
            "verify-projection");
        var draft = Draft(command.DraftId.Value, new FeatureVerification(digest, 1, 1, 0, 0, Now));
        var release = Release(digest);

        AssertProjectionRejected("ProjectVerification", command, Review(
            draft,
            release with { Digest = Digest('c') }));
        AssertProjectionRejected("ProjectVerification", command, Review(
            Draft(command.DraftId.Value, null),
            release));
        AssertProjectionRejected("ProjectVerification", command, Review(
            Draft(command.DraftId.Value, new FeatureVerification(digest, 2, 1, 1, 0, Now)),
            release));
        AssertProjectionRejected("ProjectVerification", command, Review(
            Draft(command.DraftId.Value, new FeatureVerification(digest, 2, 1, 0, 1, Now)),
            release));
        AssertProjectionRejected("ProjectVerification", command, Review(
            draft,
            release with { RequestedCapabilities = Enumerable.Range(0, 65).Select(index => $"capability-{index}").ToArray() }));
        AssertProjectionRejected("ProjectVerification", command, Review(
            draft,
            release with { Dependencies = ["dependency", "dependency"] }));
        AssertProjectionRejected("ProjectVerification", command, Review(
            DraftState(command.DraftId, "draft", draft.Verification, null, 99),
            release));
        AssertProjectionRejected("ProjectVerification", command, Review(
            draft,
            release with { SourceKind = FeatureSourceKind.Repository }));
    }

    [Fact]
    public void Public_projection_rejects_verification_evidence_exceeding_two_MiB()
    {
        var scenarios = Enumerable.Range(0, 1024)
            .Select(index => new FeatureScenarioEvidence(
                $"scenario-{index:D4}",
                "Oversized evidence",
                FeatureScenarioOutcome.Failed,
                new string('f', 2048),
                1))
            .ToArray();
        var evidence = new FeatureVerificationEvidence(
            $"sha256:{new string('a', 64)}",
            scenarios.Length,
            0,
            scenarios.Length,
            0,
            scenarios,
            []);

        AssertProjectionRejected("ValidateEvidenceOutput", evidence);
    }

    [Fact]
    public void Public_projection_recounts_outcomes_and_rejects_failure_text_on_passing_scenarios()
    {
        var sourceReference = $"sha256:{new string('a', 64)}";
        AssertProjectionRejected(
            "ValidateEvidenceOutput",
            new FeatureVerificationEvidence(
                sourceReference,
                1,
                1,
                0,
                0,
                [new FeatureScenarioEvidence("scenario-pass", "Pass", FeatureScenarioOutcome.Passed, "unexpected failure", 1)],
                []));
        AssertProjectionRejected(
            "ValidateEvidenceOutput",
            new FeatureVerificationEvidence(
                sourceReference,
                1,
                1,
                0,
                0,
                [new FeatureScenarioEvidence("scenario-fail", "Fail", FeatureScenarioOutcome.Failed, "expected failure", 1)],
                []));
    }

    [Fact]
    public void Install_projection_binds_all_coordinates_and_revalidates_authority_collections()
    {
        var installed = Installed("projection");
        var command = Command(installed);
        var actor = installed.Authority.ActorId;

        AssertProjectionRejected("ProjectInstallation", command, actor, installed with
        {
            Registration = installed.Registration with
            {
                InstallationId = new FeatureInstallationId("different-installation")
            }
        });
        AssertProjectionRejected("ProjectInstallation", command, actor, installed with
        {
            Authority = installed.Authority with
            {
                ActiveGrants = Enumerable.Range(0, 33)
                    .Select(index => Grant($"capability-{index}"))
                    .ToArray()
            }
        });
        AssertProjectionRejected("ProjectInstallation", command, actor, installed with
        {
            Registration = installed.Registration with { Subscriptions = ["feature.input", "feature.input"] }
        });
        AssertProjectionRejected("ProjectInstallation", command, actor, installed with
        {
            Authority = installed.Authority with
            {
                ActiveGrants =
                [
                    Grant("capability.read") with
                    {
                        ConstraintsJson = "{\"allowedToolIds\":[\"capability.read\"],\"payload\":{\"Client-Secret\":[\"response-credential-canary\"]}}"
                    }
                ]
            }
        });
        AssertProjectionRejected("ProjectInstallation", command, actor, installed with
        {
            Release = installed.Release with { RequestedCapabilities = ["capability.different"] }
        });
        AssertProjectionRejected("ProjectInstallation", command, actor, installed with
        {
            Release = installed.Release with { SourceKind = FeatureSourceKind.Repository }
        });
    }

    [Fact]
    public void Explicit_rollback_availability_controls_detail_hydration_and_both_public_advertisements()
    {
        var installed = Installed("explicit-rollback-availability");
        var previous = Release(Digest('e')) with { Source = Source("explicit-rollback-previous") };
        installed = installed with
        {
            Release = installed.Release with { Source = installed.Draft.Source },
            Authority = installed.Authority with
            {
                PreviousRelease = previous.Digest,
                ExactRollbackAvailable = false
            }
        };
        var detail = new InstalledFeatureDetail(
            installed.Draft,
            installed.Release,
            null,
            installed.Authority,
            installed.Registration,
            7);

        var installReply = InvokeProjection<FeatureInstallReply>(
            "ProjectInstallation",
            Command(installed),
            installed.Authority.ActorId,
            installed);
        var featureReply = InvokeProjection<FeatureReply>(
            "ProjectFeature",
            installed.Draft.DraftId,
            installed.Authority.ActorId,
            detail);

        Assert.False(installReply.RollbackAvailable);
        Assert.False(featureReply.RollbackAvailable);
        Assert.Null(featureReply.PreviousRelease);
        AssertProjectionRejected(
            "ProjectFeature",
            installed.Draft.DraftId,
            installed.Authority.ActorId,
            detail with { PreviousRelease = previous });
        AssertProjectionRejected(
            "ProjectFeature",
            installed.Draft.DraftId,
            installed.Authority.ActorId,
            detail with
            {
                Authority = installed.Authority with { ExactRollbackAvailable = true }
            });
    }

    [Fact]
    public void Composite_Feature_replies_publish_source_metadata_without_duplicate_bodies()
    {
        var installed = Installed("source-ownership");
        var previous = Release(Digest('e')) with { Source = Source("previous-source-ownership") };
        installed = installed with
        {
            Release = installed.Release with { Source = installed.Draft.Source },
            Authority = installed.Authority with
            {
                PreviousRelease = previous.Digest,
                ExactRollbackAvailable = true
            }
        };
        var verificationEvidence = Evidence(installed.Release);
        var verificationDraft = Draft(
            "draft-verification-source-ownership",
            new FeatureVerification(installed.Release.Digest, 1, 1, 0, 0, Now, verificationEvidence));
        var verificationRelease = installed.Release with { Source = verificationDraft.Source };
        var verificationCommand = new VerifyFeatureDraft(
            verificationDraft.DraftId,
            verificationDraft.Revision - 1,
            "verify-source-ownership");
        var verification = InvokeProjection<FeatureReleaseReviewReply>(
            "ProjectVerification",
            verificationCommand,
            new FeatureVerificationReview(verificationDraft, verificationRelease, verificationEvidence, Now));
        var accessCommand = new PrepareFeatureAccessReview(
            installed.Draft.DraftId,
            installed.Draft.Revision,
            installed.Registration.InstallationId,
            installed.Release.Digest,
            installed.Authority.ActiveGrants,
            installed.Registration.Subscriptions);
        var access = InvokeProjection<FeatureAccessReviewReply>(
            "ProjectAccessReview",
            accessCommand,
            new FeatureAccessReview(
                new VerifiedFeatureCandidate(installed.Draft, installed.Release),
                installed.Registration.InstallationId,
                installed.Authority.ActiveGrants,
                installed.Registration.Subscriptions,
                previous));
        var installation = InvokeProjection<FeatureInstallReply>(
            "ProjectInstallation",
            Command(installed),
            installed.Authority.ActorId,
            installed);
        var detail = InvokeProjection<FeatureReply>(
            "ProjectFeature",
            installed.Draft.DraftId,
            installed.Authority.ActorId,
            new InstalledFeatureDetail(
                installed.Draft,
                installed.Release,
                previous,
                installed.Authority,
                installed.Registration,
                7));

        Assert.Null(verification.Draft.Source);
        Assert.Empty(verification.Draft.Verification.Scenarios);
        Assert.Empty(verification.Draft.Verification.Artifacts);
        Assert.Single(verification.Verification.Scenarios);
        Assert.Null(verification.Release.Source);
        Assert.NotNull(access.Draft.Source);
        Assert.Null(access.Release.Source);
        Assert.Null(access.PreviousRelease.Source);
        Assert.NotNull(installation.Draft.Source);
        Assert.Null(installation.Release.Source);
        Assert.Null(detail.ActiveRelease.Source);
        Assert.Null(detail.PreviousRelease.Source);
        Assert.Equal(7, detail.Revision);
    }

    [Fact]
    public void Dedicated_Feature_release_source_projection_binds_every_authority_coordinate()
    {
        var installed = Installed("source-coordinate");
        installed = installed with { Release = installed.Release with { Source = installed.Draft.Source } };
        var detail = new InstalledFeatureDetail(
            installed.Draft,
            installed.Release,
            null,
            installed.Authority,
            installed.Registration,
            7);
        var reply = InvokeProjection<FeatureReleaseSourceReply>(
            "ProjectFeatureReleaseSource",
            installed.Draft.DraftId,
            installed.Registration.InstallationId,
            installed.Release.Digest,
            installed.Release.SourceReference,
            installed.Authority.ActorId,
            detail);

        Assert.Equal(installed.Draft.DraftId.Value, reply.FeatureId);
        Assert.Equal(installed.Registration.InstallationId.Value, reply.InstallationId);
        Assert.Equal(installed.Release.Digest.Value, reply.ReleaseDigest);
        Assert.Equal(installed.Release.SourceReference, reply.SourceReference);
        Assert.Equal(installed.Draft.Source.Files.Select(file => file.Content), reply.Source.Files.Select(file => file.Content));
        AssertProjectionRejected(
            "ProjectFeatureReleaseSource",
            new FeatureDraftId("different-feature"),
            installed.Registration.InstallationId,
            installed.Release.Digest,
            installed.Release.SourceReference,
            installed.Authority.ActorId,
            detail);
        AssertProjectionRejected(
            "ProjectFeatureReleaseSource",
            installed.Draft.DraftId,
            new FeatureInstallationId("different-installation"),
            installed.Release.Digest,
            installed.Release.SourceReference,
            installed.Authority.ActorId,
            detail);
        AssertProjectionRejected(
            "ProjectFeatureReleaseSource",
            installed.Draft.DraftId,
            installed.Registration.InstallationId,
            Digest('f'),
            installed.Release.SourceReference,
            installed.Authority.ActorId,
            detail);
        AssertProjectionRejected(
            "ProjectFeatureReleaseSource",
            installed.Draft.DraftId,
            installed.Registration.InstallationId,
            installed.Release.Digest,
            $"sha256:{new string('f', 64)}",
            installed.Authority.ActorId,
            detail);
        AssertProjectionRejected(
            "ProjectFeatureReleaseSource",
            installed.Draft.DraftId,
            installed.Registration.InstallationId,
            installed.Release.Digest,
            installed.Release.SourceReference,
            new ActorId("different-actor"),
            detail);
    }

    private static void AssertProjectionRejected(string methodName, params object[] arguments)
    {
        var method = typeof(DigitalBrainUiEndpoints).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, arguments));
        Assert.NotNull(exception.InnerException);
        Assert.True(
            exception.InnerException is InvalidDataException or ArgumentException,
            exception.InnerException.GetType().FullName);
        Assert.DoesNotContain("response-credential-canary", exception.InnerException.Message, StringComparison.Ordinal);
    }

    private static T InvokeProjection<T>(string methodName, params object[] arguments)
    {
        var method = typeof(DigitalBrainUiEndpoints).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method.Invoke(null, arguments));
    }

    private static object RevisionInput(ReviseFeatureDraftRequest request)
    {
        var method = typeof(DigitalBrainUiEndpoints).GetMethod(
            "MapRevision",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method.Invoke(null, [request]) ?? throw new Xunit.Sdk.XunitException("Revision input was not mapped.");
    }

    private static FeatureDraft Draft(string id, FeatureVerification? verification) => new(
        new FeatureDraftId(id),
        new OriginatingRequest("operation-projection", "conversation-projection", "Project a safe Feature"),
        "Project a safe Feature",
        "draft",
        new FeatureBehavior([
            new FeatureScenario("scenario-projection", "Projection", "a result exists", "it is projected", "coordinates remain exact")
        ]),
        Source(),
        verification,
        null,
        4,
        Now.AddMinutes(-1),
        Now);

    private static FeatureDraft DraftState(
        FeatureDraftId draftId,
        string status,
        FeatureVerification? verification,
        FeatureInstallationId? installationId,
        long revision) => new(
        draftId,
        new OriginatingRequest("operation-projection-state", "conversation-projection-state", "Project a state-safe Feature"),
        "Project a state-safe Feature",
        status,
        new FeatureBehavior([
            new FeatureScenario("scenario-projection-state", "State", "a result exists", "state is checked", "impossible state is rejected")
        ]),
        Source(),
        verification,
        installationId,
        revision,
        Now.AddMinutes(-1),
        Now);

    private static FeatureDraft Rewrite(
        FeatureDraft draft,
        FeatureBehavior? behavior = null,
        FeatureSourceSnapshot? source = null,
        long? revision = null) => new(
        draft.DraftId,
        draft.OriginatingRequest,
        draft.Goal,
        draft.Status,
        behavior ?? draft.Behavior,
        source ?? draft.Source,
        draft.Verification,
        draft.InstallationId,
        revision ?? draft.Revision,
        draft.CreatedAt,
        draft.UpdatedAt);

    private static FeatureDraftPatch Patch(FeatureDraftId draftId, long revision) => new(
        "patch-projection",
        draftId,
        revision,
        "Replace the Feature safely",
        new FeatureBehavior([
            new FeatureScenario("scenario-patch-projection", "Patch", "a Draft exists", "a patch is projected", "the patch remains exact")
        ]),
        Source());

    private static FeatureBehavior DomainBehavior(string suffix) => new([
        new FeatureScenario(
            $"scenario-{suffix}",
            $"Scenario {suffix}",
            $"a {suffix} Draft exists",
            $"the {suffix} command runs",
            $"the {suffix} content remains exact")
    ]);

    private static GrpcFeatureBehavior GrpcBehavior(string suffix) => new()
    {
        Scenarios =
        {
            new GrpcFeatureScenario
            {
                ScenarioId = $"scenario-{suffix}",
                Name = $"Scenario {suffix}",
                Given = $"a {suffix} Draft exists",
                When = $"the {suffix} command runs",
                Then = $"the {suffix} content remains exact"
            }
        }
    };

    private static FeatureSourceSnapshot Source(string suffix = "projection") => new(
        $"src/{suffix}/Feature.csproj",
        $"tests/{suffix}.Scenarios/Feature.Scenarios.csproj",
        [
            new FeatureSourceFile($"src/{suffix}/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile($"tests/{suffix}.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
        ]);

    private static GrpcFeatureSourceSnapshot GrpcSource(string suffix)
    {
        var source = new GrpcFeatureSourceSnapshot
        {
            ImplementationProjectPath = $"src/{suffix}/Feature.csproj",
            ScenarioProjectPath = $"tests/{suffix}.Scenarios/Feature.Scenarios.csproj"
        };
        source.Files.Add([
            new GrpcFeatureSourceFile
            {
                Path = source.ImplementationProjectPath,
                Content = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"
            },
            new GrpcFeatureSourceFile
            {
                Path = source.ScenarioProjectPath,
                Content = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"
            }
        ]);
        return source;
    }

    private static FeatureReleaseMetadata Release(ReleaseDigest digest) => new(
        digest,
        $"sha256:{digest.Value}",
        FeatureSourceKind.RuntimeAuthored,
        ["capability.read"],
        ["dependency.read"]);

    private static FeatureVerificationReview Review(FeatureDraft draft, FeatureReleaseMetadata release) => new(
        draft,
        release,
        Evidence(release),
        draft.Verification?.VerifiedAt ?? Now);

    private static FeatureVerificationEvidence Evidence(FeatureReleaseMetadata release) => new(
        release.SourceReference,
        1,
        1,
        0,
        0,
        [new FeatureScenarioEvidence("scenario-projection", "Projection", FeatureScenarioOutcome.Passed, null, 1)],
        []);

    private static FeatureGrantSpec Grant(string capabilityId) => new(
        capabilityId,
        1,
        null,
        $"{{\"allowedToolIds\":[\"{capabilityId}\"]}}");

    private static InstalledFeatureVersion Installed(string suffix)
    {
        var installationId = new FeatureInstallationId($"installation-{suffix}");
        var release = Release(Digest('d'));
        var grant = Grant("capability.read");
        var draft = new FeatureDraft(
            new FeatureDraftId($"draft-install-{suffix}"),
            new OriginatingRequest("operation-install-projection", "conversation-install-projection", "Project an installed Feature"),
            "Project an installed Feature",
            "installed",
            new FeatureBehavior([
                new FeatureScenario("scenario-install-projection", "Install", "a Feature is verified", "it is installed", "the coordinates remain exact")
            ]),
            Source(),
            new FeatureVerification(release.Digest, 1, 1, 0, 0, Now),
            installationId,
            5,
            Now.AddMinutes(-1),
            Now);
        return new InstalledFeatureVersion(
            draft,
            release,
            new FeatureAuthoritySnapshot(
                installationId,
                new ActorId("actor-projection"),
                release.Digest,
                null,
                new GrantRevision(1),
                [grant],
                null,
                null,
                [],
                false,
                null),
            new FeatureInstallationRegistration(installationId, release.Digest, ["feature.input"]));
    }

    private static InstallFeatureVersion Command(InstalledFeatureVersion installed) => new(
        installed.Draft.DraftId,
        installed.Draft.Revision - 1,
        installed.Registration.InstallationId,
        installed.Release.Digest,
        installed.Authority.ActiveGrants,
        installed.Registration.Subscriptions,
        "decision-projection",
        "install-projection");

    private static ReleaseDigest Digest(char value) => new(new string(value, 64));
}
