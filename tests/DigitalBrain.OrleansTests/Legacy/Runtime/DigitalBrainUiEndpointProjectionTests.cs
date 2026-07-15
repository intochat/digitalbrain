extern alias McpProject;

using System.Reflection;
using DigitalBrain.Kernel.Contracts;
using AcceptSuggestedChangeInput = McpProject::DigitalBrain.V2.Ui.Grpc.AcceptSuggestedChangeInput;
using DigitalBrainUiEndpoints = McpProject::DigitalBrain.Mcp.DigitalBrainUiEndpoints;
using GrpcFeatureBehavior = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureBehavior;
using GrpcFeatureDraftPatch = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureDraftPatch;
using GrpcFeatureScenario = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureScenario;
using GrpcFeatureSourceFile = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceFile;
using GrpcFeatureSourceSnapshot = McpProject::DigitalBrain.V2.Ui.Grpc.FeatureSourceSnapshot;
using RejectSuggestedChangeInput = McpProject::DigitalBrain.V2.Ui.Grpc.RejectSuggestedChangeInput;
using ReviseFeatureBehaviorInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureBehaviorInput;
using ReviseFeatureDraftRequest = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureDraftRequest;
using ReviseFeatureSourceInput = McpProject::DigitalBrain.V2.Ui.Grpc.ReviseFeatureSourceInput;

namespace DigitalBrain.Tests.Runtime;

public sealed class DigitalBrainUiEndpointProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Draft_projection_binds_the_requested_identity_and_validates_Verification()
    {
        var release = Digest('a');
        var valid = Draft("draft-projection", new FeatureVerification(release, 2, 2, 0, 0, Now));

        AssertProjectionRejected("ProjectDraft", new FeatureDraftId("different-draft"), valid);
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            Draft("draft-projection", new FeatureVerification(release, 2, 1, 0, 0, Now)));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            Draft("draft-projection", new FeatureVerification(
                release,
                1,
                1,
                0,
                0,
                Now.ToOffset(TimeSpan.FromHours(1)))));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            DraftState(valid.DraftId, "installed", null, null, 4));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            DraftState(
                valid.DraftId,
                "draft",
                new FeatureVerification(release, 1, 1, 0, 0, Now),
                new FeatureInstallationId("unexpected-installation"),
                4));
        AssertProjectionRejected(
            "ProjectDraft",
            valid.DraftId,
            DraftState(
                valid.DraftId,
                "installed",
                new FeatureVerification(release, 2, 1, 1, 0, Now),
                new FeatureInstallationId("installed-with-failed-verification"),
                4));
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

        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            draft,
            release with { Digest = Digest('c') }));
        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            Draft(command.DraftId.Value, null),
            release));
        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            Draft(command.DraftId.Value, new FeatureVerification(digest, 2, 1, 1, 0, Now)),
            release));
        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            Draft(command.DraftId.Value, new FeatureVerification(digest, 2, 1, 0, 1, Now)),
            release));
        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            draft,
            release with { RequestedCapabilities = Enumerable.Range(0, 65).Select(index => $"capability-{index}").ToArray() }));
        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            draft,
            release with { Dependencies = ["dependency", "dependency"] }));
        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            DraftState(command.DraftId, "draft", draft.Verification, null, 99),
            release));
        AssertProjectionRejected("ProjectVerification", command, new VerifiedFeatureCandidate(
            draft,
            release with { SourceKind = FeatureSourceKind.Repository }));
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
        "runtime-authored-projection",
        FeatureSourceKind.RuntimeAuthored,
        ["capability.read"],
        ["dependency.read"]);

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
