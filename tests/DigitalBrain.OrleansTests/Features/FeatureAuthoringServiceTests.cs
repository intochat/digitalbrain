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
using FeatureScenarioResult = DigitalBrain.FeatureBuilder.FeatureScenarioResult;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.OrleansTests.Features;

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class FeatureAuthoringServiceTests(FeatureGrainClusterFixture fixture)
{
    [Fact]
    public async Task Verification_builds_only_the_stored_Source_and_persists_the_trusted_result()
    {
        var context = Context("owner-authoring-verify");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-authoring-verify",
            "Build the persisted Feature",
            fixture.Time.GetUtcNow(),
            "conversation-authoring-verify"));
        var source = Source("stored");
        draft = await hub.ReviseSourceAsync(new ReviseFeatureSource(
            draft.DraftId,
            source,
            draft.Revision,
            "source-authoring-verify",
            fixture.Time.GetUtcNow().AddMinutes(1)));
        var release = Release("a1", "capability.read");
        var builds = new RecordingBuildEndpoint(new FeatureBuildArtifact(release, new FeatureScenarioResult(3, 3, 0, 0)));
        var service = Service(builds);

        var candidate = await service.VerifyAsync(
            context,
            new VerifyFeatureDraft(draft.DraftId, draft.Revision, "verify-authoring"));

        var submission = Assert.IsType<FeatureBuildSubmission>(builds.LastSubmission);
        Assert.Equal(source.ImplementationProjectPath, submission.ImplementationProjectPath);
        Assert.Equal(source.ScenarioProjectPath, submission.ScenarioProjectPath);
        Assert.Equal(source.Files.Select(file => (file.Path, file.Content)), submission.Files.Select(file => (file.Path, file.Content)));
        Assert.Equal(FeatureSourceKind.RuntimeAuthored, submission.SourceKind);
        Assert.Equal(release, candidate.Release);
        Assert.Equal(release.Digest, candidate.Draft.Verification?.Release);
        Assert.Equal(3, candidate.Draft.Verification?.Passed);
        Assert.Equal(0, candidate.Draft.Verification?.Failed);
    }

    [Fact]
    public async Task Verification_rejects_cross_Owner_and_stale_Drafts_before_building()
    {
        var context = Context("owner-authoring-verify-authority");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-authoring-verify-authority",
            "Verify with owner authority",
            fixture.Time.GetUtcNow(),
            "conversation-authoring-verify-authority"));
        var builds = new RecordingBuildEndpoint(new FeatureBuildArtifact(Release("a2"), new FeatureScenarioResult(1, 1, 0, 0)));
        var service = Service(builds);
        var command = new VerifyFeatureDraft(draft.DraftId, draft.Revision, "verify-authority");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.VerifyAsync(Context("owner-authoring-verify-other"), command));
        await hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
            draft.DraftId,
            new FeatureBehavior([new FeatureScenario("scenario-new", "New", "a Draft exists", "it changes", "the revision advances")]),
            draft.Revision,
            "behavior-authoring-verify-new",
            fixture.Time.GetUtcNow().AddMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAsync(context, command));

        Assert.Equal(0, builds.CallCount);
    }

    [Fact]
    public async Task A_failed_build_never_records_Verification()
    {
        var context = Context("owner-authoring-verify-failure");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-authoring-verify-failure",
            "Reject a failed build",
            fixture.Time.GetUtcNow(),
            "conversation-authoring-verify-failure"));
        var builds = new RecordingBuildEndpoint(new InvalidOperationException("scenario failure"));
        var service = Service(builds);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAsync(
            context,
            new VerifyFeatureDraft(draft.DraftId, draft.Revision, "verify-failure")));

        Assert.Null((await hub.ReadDraftAsync(draft.DraftId))?.Verification);
    }

    [Fact]
    public async Task A_concurrent_edit_after_build_prevents_the_old_release_from_becoming_current_Verification()
    {
        var context = Context("owner-authoring-concurrent-build");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-authoring-concurrent-build",
            "Reject a stale build result",
            fixture.Time.GetUtcNow(),
            "conversation-authoring-concurrent-build"));
        var artifact = new FeatureBuildArtifact(Release("a4"), new FeatureScenarioResult(1, 1, 0, 0));
        var builds = new EditingBuildEndpoint(artifact, () => hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
            draft.DraftId,
            new FeatureBehavior([new FeatureScenario("scenario-concurrent", "Concurrent", "a build is running", "the Draft changes", "the build becomes stale")]),
            draft.Revision,
            "behavior-concurrent-build",
            fixture.Time.GetUtcNow().AddMinutes(1))));
        var service = new FeatureAuthoringService(
            fixture.Cluster.Client,
            builds,
            new StaticArtifactCatalog(artifact.Release),
            new UnusedLifecycleRail(),
            fixture.Time);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAsync(
            context,
            new VerifyFeatureDraft(draft.DraftId, draft.Revision, "verify-concurrent-build")));

        var current = Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId));
        Assert.Equal(draft.Revision + 1, current.Revision);
        Assert.Null(current.Verification);
    }

    [Fact]
    public async Task A_lost_verification_response_may_rebuild_the_same_snapshot_and_replay_the_same_candidate()
    {
        var context = Context("owner-authoring-verify-replay");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-authoring-verify-replay",
            "Replay a deterministic verification",
            fixture.Time.GetUtcNow(),
            "conversation-authoring-verify-replay"));
        var artifact = new FeatureBuildArtifact(Release("a5"), new FeatureScenarioResult(1, 1, 0, 0));
        var builds = new RecordingBuildEndpoint(artifact);
        var service = Service(builds);
        var command = new VerifyFeatureDraft(draft.DraftId, draft.Revision, "verify-replay");

        var first = await service.VerifyAsync(context, command);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        var replay = await service.VerifyAsync(context, command);

        Assert.Equal(2, builds.CallCount);
        Assert.Equal(first.Release, replay.Release);
        Assert.Equal(first.Draft.Revision, replay.Draft.Revision);
        Assert.Equal(first.Draft.Verification, replay.Draft.Verification);
    }

    [Fact]
    public async Task Returned_failed_scenarios_are_not_recorded_as_Verification()
    {
        var context = Context("owner-authoring-scenario-failure");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-authoring-scenario-failure",
            "Reject failed scenarios",
            fixture.Time.GetUtcNow(),
            "conversation-authoring-scenario-failure"));
        var service = Service(new RecordingBuildEndpoint(new FeatureBuildArtifact(Release("a6"), new FeatureScenarioResult(2, 1, 1, 0))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerifyAsync(
            context,
            new VerifyFeatureDraft(draft.DraftId, draft.Revision, "verify-scenario-failure")));

        Assert.Null((await hub.ReadDraftAsync(draft.DraftId))?.Verification);
    }

    [Fact]
    public async Task Read_accept_and_reject_are_explicit_owner_scoped_authoring_operations()
    {
        var context = Context("owner-authoring-patch");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-authoring-patch",
            "Review an explicit patch",
            fixture.Time.GetUtcNow(),
            "conversation-authoring-patch"));
        var patch = new FeatureDraftPatch(
            "patch-authoring-service",
            draft.DraftId,
            draft.Revision,
            "Replace the reviewed Draft",
            new FeatureBehavior([new FeatureScenario("scenario-patch", "Patch", "a Draft exists", "the patch is accepted", "both replacements apply")]),
            Source("patch"));
        var service = Service(new RecordingBuildEndpoint(new InvalidOperationException("unused")));

        Assert.Equal(draft.DraftId, (await service.ReadAsync(context, draft.DraftId)).DraftId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReadAsync(Context("owner-authoring-patch-other"), draft.DraftId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AcceptSuggestedChangeAsync(
            Context(context.OwnerId.Value, []),
            new AcceptSuggestedChange(patch, draft.Revision, "accept-authoring-unauthorized", fixture.Time.GetUtcNow())));
        var rejected = await service.RejectSuggestedChangeAsync(context, new RejectSuggestedChange(
            draft.DraftId,
            patch.PatchId,
            patch.BaseRevision,
            draft.Revision));
        var accepted = await service.AcceptSuggestedChangeAsync(context, new AcceptSuggestedChange(
            patch,
            draft.Revision,
            "accept-authoring",
            fixture.Time.GetUtcNow().AddMinutes(1)));

        Assert.Equal(draft.Revision, rejected.Revision);
        Assert.Equal(draft.Revision + 1, accepted.Revision);
        Assert.Equal(patch.ReplacementBehavior.Scenarios, accepted.Behavior.Scenarios);
        Assert.Equal(patch.ReplacementSource.Files, accepted.Source.Files);
    }

    private FeatureAuthoringService Service(RecordingBuildEndpoint builds) => new(
        fixture.Cluster.Client,
        builds,
        new StaticArtifactCatalog(Release("a3")),
        new UnusedLifecycleRail(),
        fixture.Time);

    private static RuntimeRequestContext Context(string owner, string[]? grants = null) => new(
        new BrainOwnerId(owner),
        new ActorId("actor-feature-author"),
        new SessionId("session-feature-author"),
        AuthAssurance.Oidc,
        "correlation-feature-author",
        null,
        new HashSet<string>(grants ?? ["feature.manage"], StringComparer.Ordinal),
        "conversation-feature-author");

    private static FeatureSourceSnapshot Source(string suffix) => new(
        $"src/Feature{suffix}/Feature{suffix}.csproj",
        $"tests/Feature{suffix}.Scenarios/Feature{suffix}.Scenarios.csproj",
        [
            new FeatureSourceFile($"src/Feature{suffix}/Feature{suffix}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile($"src/Feature{suffix}/Feature{suffix}.cs", "namespace RuntimeAuthored; public sealed class Feature;"),
            new FeatureSourceFile($"tests/Feature{suffix}.Scenarios/Feature{suffix}.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
        ]);

    private static FeatureReleaseMetadata Release(string suffix, params string[] capabilities) => new(
        new ReleaseDigest(new string('0', 63) + suffix[^1]),
        $"runtime-authored-{suffix}",
        FeatureSourceKind.RuntimeAuthored,
        capabilities,
        []);

    private sealed class RecordingBuildEndpoint : FeatureBuildEndpoint
    {
        private readonly FeatureBuildArtifact? _artifact;
        private readonly Exception? _exception;

        public RecordingBuildEndpoint(FeatureBuildArtifact artifact) => _artifact = artifact;
        public RecordingBuildEndpoint(Exception exception) => _exception = exception;
        public int CallCount { get; private set; }
        public FeatureBuildSubmission? LastSubmission { get; private set; }

        public Task<FeatureBuildArtifact> BuildAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastSubmission = submission;
            return _exception is null ? Task.FromResult(_artifact!) : Task.FromException<FeatureBuildArtifact>(_exception);
        }
    }

    private sealed class EditingBuildEndpoint(FeatureBuildArtifact artifact, Func<Task<FeatureDraft>> edit) : FeatureBuildEndpoint
    {
        public async Task<FeatureBuildArtifact> BuildAsync(FeatureBuildSubmission submission, CancellationToken cancellationToken = default)
        {
            await edit();
            return artifact;
        }
    }

    private sealed class StaticArtifactCatalog(FeatureReleaseMetadata release) : FeatureArtifactCatalog
    {
        public Task<FeatureReleaseMetadata> DemandReleaseAsync(ReleaseDigest digest, CancellationToken cancellationToken = default) =>
            Task.FromResult(release);
    }

    private sealed class UnusedLifecycleRail : FeatureLifecycleRail
    {
        public Task<FeatureApprovalSnapshot> ProposeAsync(RuntimeRequestContext context, FeatureReleaseProposal proposal, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> DecideAsync(RuntimeRequestContext context, FeatureApprovalDecision decision, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> GrantAsync(RuntimeRequestContext context, FeatureInstallationId installationId, ReleaseDigest release, FeatureGrantSpec[] grants, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> InstallAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FeatureLifecycleInspection> InspectAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FeatureLifecycleInspection(0, [], [], Array.Empty<FeatureInstallationInspection>(), Array.Empty<FeatureInstallationRegistration>()));
    }
}
