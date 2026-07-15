extern alias McpProject;

using System.Text.Json;
using DigitalBrain.Kernel.Contracts.Runtime;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
using DigitalBrainRun = McpProject::DigitalBrain.Mcp.DigitalBrainRun;
using DigitalBrainQueryService = McpProject::DigitalBrain.Mcp.DigitalBrainQueryService;
using FeatureInstallationInspection = McpProject::DigitalBrain.Mcp.FeatureInstallationInspection;
using FeatureLifecycleInspection = McpProject::DigitalBrain.Mcp.FeatureLifecycleInspection;
using FeatureLifecycleRail = McpProject::DigitalBrain.Mcp.IFeatureLifecycleRail;
using FeatureRunInstallationInspection = McpProject::DigitalBrain.Mcp.FeatureRunInstallationInspection;
using FeatureRunLifecycleInspection = McpProject::DigitalBrain.Mcp.FeatureRunLifecycleInspection;
using ProductionFeatureLifecycleRail = McpProject::DigitalBrain.Mcp.FeatureLifecycleRail;

namespace DigitalBrain.OrleansTests.Features;

public sealed class DigitalBrainQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReleaseDigest ReleaseOne = new(new string('a', 64));
    private static readonly ReleaseDigest ReleaseTwo = new(new string('b', 64));
    private static readonly ReleaseDigest ReleaseThree = new(new string('c', 64));

    [Fact]
    public async Task List_runs_applies_filters_and_deterministic_activity_order()
    {
        var installationId = new FeatureInstallationId("installation-activity-order");
        var completed = Run(
            "run-a-completed",
            installationId,
            ReleaseOne,
            FeatureRunOrigin.Chat,
            FeatureRunStatus.Completed,
            Now,
            Now.AddMinutes(30));
        var queued = Run(
            "run-b-queued",
            installationId,
            ReleaseOne,
            FeatureRunOrigin.Direct,
            FeatureRunStatus.Queued,
            Now.AddMinutes(20));
        var failed = Run(
            "run-c-failed",
            installationId,
            ReleaseOne,
            FeatureRunOrigin.Event,
            FeatureRunStatus.Failed,
            Now.AddMinutes(20));
        var inspection = Inspection(
            installationId,
            "feature-activity-order",
            "Prepare an activity digest",
            "actor-activity-order",
            ReleaseOne,
            [failed, queued, completed]);
        var rail = new FixedLifecycleRail(inspection);
        var service = new DigitalBrainQueryService(rail);
        var context = Context("owner-activity-order", "actor-activity-order");

        var all = await service.ListRunsAsync(context, limit: 10);
        var filtered = await service.ListRunsAsync(
            context,
            FeatureRunStatus.Failed,
            FeatureRunOrigin.Event,
            new FeatureDraftId("feature-activity-order"),
            10);

        Assert.Equal(
            ["run-a-completed", "run-b-queued", "run-c-failed"],
            all.Select(candidate => candidate.Run.RunId));
        Assert.Equal("run-c-failed", Assert.Single(filtered).Run.RunId);
        Assert.Equal(FeatureRunStatus.Failed, rail.LastRunRequest?.Status);
        Assert.Equal(FeatureRunOrigin.Event, rail.LastRunRequest?.Origin);
        Assert.Equal(10, rail.LastRunRequest?.Limit);
    }

    [Fact]
    public async Task Get_run_returns_the_identical_safe_projection_and_missing_runs_are_not_found()
    {
        var installationId = new FeatureInstallationId("installation-activity-detail");
        var run = Run(
            "run-activity-detail",
            installationId,
            ReleaseOne,
            FeatureRunOrigin.Schedule,
            FeatureRunStatus.Completed,
            Now,
            Now.AddMinutes(1));
        var service = Service([
            Inspection(
                installationId,
                "feature-activity-detail",
                "Send the daily digest",
                "actor-activity-detail",
                ReleaseOne,
                [run],
                "runtime-state-secret")
        ]);
        var context = Context("owner-activity-detail", "actor-activity-detail");

        var listed = Assert.Single(await service.ListRunsAsync(context));
        var detail = await service.GetRunAsync(context, run.RunId);

        Assert.Equal(listed, detail);
        Assert.Equal("feature-activity-detail", detail.FeatureId.Value);
        Assert.Equal("Send the daily digest", detail.FeatureGoal);
        Assert.DoesNotContain("runtime-state-secret", JsonSerializer.Serialize(detail), StringComparison.Ordinal);
        Assert.Equal(["FeatureId", "FeatureGoal", "Run"], typeof(DigitalBrainRun).GetProperties().Select(property => property.Name));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetRunAsync(context, "run-activity-missing"));
    }

    [Fact]
    public async Task Activity_queries_do_not_use_the_full_lifecycle_inspection()
    {
        var installationId = new FeatureInstallationId("installation-activity-projection-only");
        var rail = new FixedLifecycleRail(Inspection(
            installationId,
            "feature-activity-projection-only",
            "Read only safe Run projections",
            "actor-activity-projection-only",
            ReleaseOne,
            [Run("run-activity-projection-only", installationId, ReleaseOne, FeatureRunOrigin.Direct, FeatureRunStatus.Queued, Now)]));
        var service = new DigitalBrainQueryService(rail);

        var visible = await service.ListRunsAsync(Context(
            "owner-activity-projection-only",
            "actor-activity-projection-only"));

        Assert.Single(visible);
        Assert.Equal(0, rail.FullInspectionReads);
        Assert.Equal(1, rail.RunInspectionReads);
        Assert.Equal(
            ["InstallationId", "ActiveRelease", "Revision", "Runs"],
            typeof(FeatureRunCollectionSnapshot).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task Queries_require_an_authenticated_owner_actor_and_ignore_other_actors_before_validation()
    {
        var installationId = new FeatureInstallationId("installation-activity-authority");
        var owned = Inspection(
            installationId,
            "feature-activity-authority",
            "Keep activity private",
            "actor-activity-owner",
            ReleaseOne,
            [Run("run-activity-private", installationId, ReleaseOne, FeatureRunOrigin.Direct, FeatureRunStatus.Queued, Now)]);
        var foreign = Inspection(
            new FeatureInstallationId("installation-activity-foreign"),
            "feature-activity-foreign",
            "Do not expose this activity",
            "actor-activity-foreign",
            ReleaseTwo,
            [Run("run-activity-private", new FeatureInstallationId("installation-activity-wrong"), ReleaseTwo, FeatureRunOrigin.Event, FeatureRunStatus.Failed, Now)]);
        var service = Service([foreign, owned]);
        var context = Context("owner-activity-authority", "actor-activity-owner");

        var visible = await service.ListRunsAsync(context);

        Assert.Equal("run-activity-private", Assert.Single(visible).Run.RunId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListRunsAsync(context with { Assurance = AuthAssurance.None }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListRunsAsync(context with { Assurance = (AuthAssurance)999 }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListRunsAsync(context with { OwnerId = default }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListRunsAsync(context with { ActorId = default }));
    }

    [Fact]
    public async Task List_runs_enforces_a_bounded_limit()
    {
        var installationId = new FeatureInstallationId("installation-activity-limit");
        var service = Service([
            Inspection(
                installationId,
                "feature-activity-limit",
                "Bound activity reads",
                "actor-activity-limit",
                ReleaseOne,
                [
                    Run("run-activity-limit-a", installationId, ReleaseOne, FeatureRunOrigin.Direct, FeatureRunStatus.Queued, Now.AddMinutes(1)),
                    Run("run-activity-limit-b", installationId, ReleaseOne, FeatureRunOrigin.Direct, FeatureRunStatus.Queued, Now)
                ])
        ]);
        var context = Context("owner-activity-limit", "actor-activity-limit");

        Assert.Single(await service.ListRunsAsync(context, limit: 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListRunsAsync(context, limit: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListRunsAsync(context, limit: DigitalBrainQueryService.MaximumListLimit + 1));
    }

    [Fact]
    public async Task Queries_reject_duplicate_run_ids_and_ambiguous_installation_coordinates()
    {
        var firstInstallationId = new FeatureInstallationId("installation-activity-duplicate-one");
        var secondInstallationId = new FeatureInstallationId("installation-activity-duplicate-two");
        var first = Inspection(
            firstInstallationId,
            "feature-activity-duplicate-one",
            "First duplicate",
            "actor-activity-duplicate",
            ReleaseOne,
            [Run("run-activity-duplicate", firstInstallationId, ReleaseOne, FeatureRunOrigin.Direct, FeatureRunStatus.Queued, Now)]);
        var second = Inspection(
            secondInstallationId,
            "feature-activity-duplicate-two",
            "Second duplicate",
            "actor-activity-duplicate",
            ReleaseTwo,
            [Run("run-activity-duplicate", secondInstallationId, ReleaseTwo, FeatureRunOrigin.Event, FeatureRunStatus.Failed, Now)]);
        var context = Context("owner-activity-duplicate", "actor-activity-duplicate");

        await Assert.ThrowsAsync<InvalidDataException>(() => Service([first, second]).ListRunsAsync(context));
        await Assert.ThrowsAsync<InvalidDataException>(() => Service([first, first]).ListRunsAsync(context));
    }

    [Fact]
    public async Task Queries_reject_mismatched_runtime_registration_and_draft_coordinates()
    {
        var installationId = new FeatureInstallationId("installation-activity-coordinate");
        var valid = Inspection(
            installationId,
            "feature-activity-coordinate",
            "Validate activity coordinates",
            "actor-activity-coordinate",
            ReleaseOne,
            [Run("run-activity-coordinate", installationId, ReleaseOne, FeatureRunOrigin.Direct, FeatureRunStatus.Queued, Now)]);
        var context = Context("owner-activity-coordinate", "actor-activity-coordinate");
        var otherInstallationId = new FeatureInstallationId("installation-activity-coordinate-other");

        await Assert.ThrowsAsync<InvalidDataException>(() => Service([
            valid with { Runtime = valid.Runtime! with { InstallationId = otherInstallationId } }
        ]).ListRunsAsync(context));
        await Assert.ThrowsAsync<InvalidDataException>(() => Service([
            valid with { Registration = valid.Registration! with { Release = ReleaseTwo } }
        ]).ListRunsAsync(context));
        await Assert.ThrowsAsync<InvalidDataException>(() => Service([
            valid with { Draft = Draft("feature-activity-coordinate", "Validate activity coordinates", otherInstallationId, ReleaseOne) }
        ]).ListRunsAsync(context));
        await Assert.ThrowsAsync<InvalidDataException>(() => Service([
            valid with { Draft = Draft("feature-activity-coordinate", "Validate activity coordinates", installationId, ReleaseTwo) }
        ]).ListRunsAsync(context));
        await Assert.ThrowsAsync<InvalidDataException>(() => Service([
            valid with { Runtime = valid.Runtime! with { InstallationId = otherInstallationId, Runs = [] } }
        ]).ListRunsAsync(context));
    }

    [Fact]
    public async Task Legacy_installations_without_a_run_projection_are_skipped()
    {
        var installationId = new FeatureInstallationId("installation-activity-legacy");
        var legacy = Inspection(
            installationId,
            "feature-activity-legacy",
            "Ignore an old runtime snapshot",
            "actor-activity-legacy",
            ReleaseOne,
            []) with
        {
            Registration = null,
            Draft = null,
            Runtime = Snapshot(installationId, ReleaseOne, null, null, "legacy-runtime-secret")
        };
        var service = Service([legacy]);

        var visible = await service.ListRunsAsync(Context("owner-activity-legacy", "actor-activity-legacy"));

        Assert.Empty(visible);
    }

    [Fact]
    public async Task Historical_run_release_is_not_rewritten_by_feature_updates()
    {
        var installationId = new FeatureInstallationId("installation-activity-history");
        var historical = Run(
            "run-activity-release-one",
            installationId,
            ReleaseOne,
            FeatureRunOrigin.Chat,
            FeatureRunStatus.Completed,
            Now,
            Now.AddMinutes(1));
        var rail = new FixedLifecycleRail(Inspection(
            installationId,
            "feature-activity-history",
            "Preserve historical versions",
            "actor-activity-history",
            ReleaseTwo,
            [historical]));
        var service = new DigitalBrainQueryService(rail);
        var context = Context("owner-activity-history", "actor-activity-history");

        var beforeUpdate = await service.GetRunAsync(context, historical.RunId);
        rail.Inspection = [Inspection(
            installationId,
            "feature-activity-history",
            "Preserve historical versions",
            "actor-activity-history",
            ReleaseThree,
            [historical])];
        var afterUpdate = await service.GetRunAsync(context, historical.RunId);

        Assert.Equal(ReleaseOne, beforeUpdate.Run.Release);
        Assert.Equal(ReleaseOne, afterUpdate.Run.Release);
    }

    private static DigitalBrainQueryService Service(FeatureInstallationInspection[] inspections) =>
        new(new FixedLifecycleRail(inspections));

    private static RuntimeRequestContext Context(string ownerId, string actorId) => new(
        new BrainOwnerId(ownerId),
        new ActorId(actorId),
        new SessionId("session-activity-query"),
        AuthAssurance.Oidc,
        "correlation-activity-query",
        null,
        new HashSet<string>(StringComparer.Ordinal));

    private static FeatureInstallationInspection Inspection(
        FeatureInstallationId installationId,
        string featureId,
        string featureGoal,
        string actorId,
        ReleaseDigest activeRelease,
        FeatureRunSnapshot[] runs,
        string stateJson = "{}")
    {
        var authority = new FeatureAuthoritySnapshot(
            installationId,
            new ActorId(actorId),
            activeRelease,
            null,
            new GrantRevision(1),
            [],
            null,
            null,
            [],
            false,
            null,
            null,
            false,
            true);
        var registration = new FeatureInstallationRegistration(installationId, activeRelease, ["manual"]);
        return new FeatureInstallationInspection(
            authority,
            registration,
            Snapshot(installationId, activeRelease, null, runs, stateJson),
            Draft(featureId, featureGoal, installationId, activeRelease));
    }

    private static FeatureInstallationSnapshot Snapshot(
        FeatureInstallationId installationId,
        ReleaseDigest activeRelease,
        ReleaseDigest? previousRelease,
        FeatureRunSnapshot[]? runs,
        string stateJson) => new(
            installationId,
            activeRelease,
            previousRelease,
            stateJson,
            false,
            null,
            [],
            null,
            [],
            [],
            [],
            1,
            [],
            null,
            runs);

    private static FeatureDraft Draft(
        string featureId,
        string featureGoal,
        FeatureInstallationId installationId,
        ReleaseDigest release)
    {
        const string implementationProject = "src/ActivityFeature/ActivityFeature.csproj";
        const string scenarioProject = "tests/ActivityFeature.Scenarios/ActivityFeature.Scenarios.csproj";
        return new FeatureDraft(
            new FeatureDraftId(featureId),
            new OriginatingRequest("operation-activity-query", "conversation-activity-query", featureGoal),
            featureGoal,
            "Installed",
            new FeatureBehavior([
                new FeatureScenario(
                    "scenario-activity-query",
                    "Project activity",
                    "an installed Feature has Runs",
                    "Activity is queried",
                    "safe Run projections are returned")
            ]),
            new FeatureSourceSnapshot(
                implementationProject,
                scenarioProject,
                [
                    new FeatureSourceFile(implementationProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                    new FeatureSourceFile(scenarioProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
                ]),
            new FeatureVerification(release, 1, 1, 0, 0, Now),
            installationId,
            3,
            Now.AddHours(-1),
            Now);
    }

    private static FeatureRunSnapshot Run(
        string runId,
        FeatureInstallationId installationId,
        ReleaseDigest release,
        FeatureRunOrigin origin,
        FeatureRunStatus status,
        DateTimeOffset occurredAt,
        DateTimeOffset? completedAt = null) => new(
            runId,
            installationId,
            release,
            origin == FeatureRunOrigin.Schedule ? "schedule.daily" : "manual",
            origin,
            OriginReference(origin),
            status,
            status == FeatureRunStatus.WaitingForApproval
                ? FeatureRunAuthorityState.WaitingForApproval
                : FeatureRunAuthorityState.Authorized,
            occurredAt,
            status == FeatureRunStatus.Queued ? null : occurredAt.AddSeconds(1),
            completedAt,
            status == FeatureRunStatus.Failed ? occurredAt.AddMinutes(1) : null,
            status == FeatureRunStatus.Queued ? 0 : 1,
            completedAt is null ? null : "result-safe-reference",
            status == FeatureRunStatus.Failed ? "The Feature could not complete." : null,
            status == FeatureRunStatus.Failed ? "Review the Feature before retrying." : null,
            "trace-safe-reference");

    private static FeatureRunOriginReference? OriginReference(FeatureRunOrigin origin) => origin switch
    {
        FeatureRunOrigin.Chat => new("conversation-activity-query", "request-activity-query", null),
        FeatureRunOrigin.Schedule => new(null, null, "automation-schedule-activity-query"),
        FeatureRunOrigin.Event => new(null, null, "automation-event-activity-query"),
        _ => null
    };

    private sealed class FixedLifecycleRail(params FeatureInstallationInspection[] inspections) : FeatureLifecycleRail
    {
        public FeatureInstallationInspection[] Inspection { get; set; } = inspections;
        public int FullInspectionReads { get; private set; }
        public int RunInspectionReads { get; private set; }
        public FeatureRunReadRequest? LastRunRequest { get; private set; }

        public Task<FeatureLifecycleInspection> InspectAsync(RuntimeRequestContext context, CancellationToken cancellationToken = default)
        {
            FullInspectionReads++;
            return Task.FromResult(new FeatureLifecycleInspection(1, [], [], Inspection, []));
        }

        public Task<FeatureRunLifecycleInspection> InspectRunsAsync(
            RuntimeRequestContext context,
            FeatureRunReadRequest request,
            CancellationToken cancellationToken = default)
        {
            RunInspectionReads++;
            LastRunRequest = request;
            var projected = Inspection.Select(candidate => new FeatureRunInstallationInspection(
                candidate.Authority,
                candidate.Registration,
                RunProjection(candidate.Runtime, request),
                candidate.Draft)).ToArray();
            return Task.FromResult(new FeatureRunLifecycleInspection(projected));
        }

        private static FeatureRunCollectionSnapshot? RunProjection(
            FeatureInstallationSnapshot? runtime,
            FeatureRunReadRequest request)
        {
            if (runtime?.Runs is not { } runs)
                return null;
            return new FeatureRunCollectionSnapshot(
                runtime.InstallationId,
                runtime.ActiveRelease,
                runtime.Revision,
                runs
                    .Where(candidate => request.Status is null || candidate.Status == request.Status)
                    .Where(candidate => request.Origin is null || candidate.Origin == request.Origin)
                    .Where(candidate => request.RunId is null || string.Equals(candidate.RunId, request.RunId, StringComparison.Ordinal))
                    .OrderByDescending(candidate => candidate.CompletedAt ?? candidate.OccurredAt)
                    .ThenByDescending(candidate => candidate.OccurredAt)
                    .ThenBy(candidate => candidate.RunId, StringComparer.Ordinal)
                    .Take(request.Limit)
                    .ToArray());
        }

        public Task<FeatureApprovalSnapshot> ProposeAsync(RuntimeRequestContext context, FeatureReleaseProposal proposal, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FeatureApprovalSnapshot> DecideAsync(RuntimeRequestContext context, FeatureApprovalDecision decision, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FeatureAuthoritySnapshot> GrantAsync(RuntimeRequestContext context, FeatureInstallationId installationId, ReleaseDigest release, FeatureGrantSpec[] grants, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FeatureAuthoritySnapshot> InstallAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FeatureAuthoritySnapshot> RollbackAsync(RuntimeRequestContext context, RollbackFeatureInstallation command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FeatureAuthoritySnapshot> RepublishAsync(RuntimeRequestContext context, FeatureInstallationRegistration registration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class FeatureLifecycleRailInspectionTests(FeatureGrainClusterFixture fixture)
{
    [Fact]
    public async Task Inspection_attaches_the_exact_installed_feature_draft()
    {
        var ownerId = new BrainOwnerId("owner-lifecycle-inspection-draft");
        var actorId = new ActorId("actor-lifecycle-inspection-draft");
        var installationId = new FeatureInstallationId("installation-lifecycle-inspection-draft");
        var release = new ReleaseDigest(new string('d', 64));
        var now = fixture.Time.GetUtcNow();
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-lifecycle-inspection-draft",
            "Attach the installed Feature Draft",
            now,
            "conversation-lifecycle-inspection-draft"));
        draft = await hub.RecordVerificationAsync(new RecordFeatureVerification(
            draft.DraftId,
            FeatureVerificationTestData.Passing(release, draft.Source, 1, now),
            draft.Revision,
            "verification-lifecycle-inspection-draft"));
        await hub.AcquireDraftInstallationReservationAsync(new InstallFeatureVersion(
            draft.DraftId,
            draft.Revision,
            installationId,
            release,
            [],
            ["manual"],
            "decision-lifecycle-inspection-draft",
            "installation-lifecycle-inspection-draft"), actorId);
        var snapshot = await hub.ReadAsync();
        var approval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                installationId,
                new FeatureReleaseMetadata(
                    release,
                    "sha256:" + release.Value,
                    FeatureSourceKind.RuntimeAuthored,
                    [],
                    []),
                []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.DecideAsync(
            new FeatureApprovalDecision(
                approval.ApprovalId,
                release,
                true,
                "decision-lifecycle-inspection-draft",
                actorId),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.GrantAsync(
            new FeatureGrantRequest(installationId, release, actorId, []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.InstallAsync(
            new FeatureInstallationRegistration(installationId, release, ["manual"]),
            snapshot.Revision);
        await fixture.PublishActiveAsync(ownerId, hub, installationId);
        var installed = await hub.MarkDraftInstalledAsync(new MarkFeatureDraftInstalled(
            draft.DraftId,
            installationId,
            release,
            draft.Revision,
            "installation-lifecycle-inspection-draft",
            now.AddMinutes(1)));
        await fixture.Grain<IFeatureInstallationGrain>(FeatureGrainIds.Installation(ownerId, installationId))
            .AppendExactAsync(
                release,
                new FeatureInput(
                    "run-lifecycle-inspection-draft",
                    "manual",
                    "{}",
                    now.AddMinutes(2),
                    "correlation-lifecycle-inspection-run",
                    "trace-lifecycle-inspection-run",
                    null,
                    FeatureRunOrigin.Direct));
        var foreignActorId = new ActorId("actor-lifecycle-inspection-foreign");
        var foreignInstallationId = new FeatureInstallationId("installation-lifecycle-inspection-foreign");
        var foreignRelease = new ReleaseDigest(new string('e', 64));
        snapshot = await hub.ReadAsync();
        var foreignApproval = await hub.ProposeAsync(
            new FeatureReleaseProposal(
                foreignInstallationId,
                new FeatureReleaseMetadata(
                    foreignRelease,
                    "sha256:" + foreignRelease.Value,
                    FeatureSourceKind.RuntimeAuthored,
                    [],
                    []),
                []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.DecideAsync(
            new FeatureApprovalDecision(
                foreignApproval.ApprovalId,
                foreignRelease,
                true,
                "decision-lifecycle-inspection-foreign",
                foreignActorId),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.GrantAsync(
            new FeatureGrantRequest(foreignInstallationId, foreignRelease, foreignActorId, []),
            snapshot.Revision);
        snapshot = await hub.ReadAsync();
        await hub.InstallAsync(
            new FeatureInstallationRegistration(foreignInstallationId, foreignRelease, ["manual"]),
            snapshot.Revision);
        await fixture.PublishActiveAsync(ownerId, hub, foreignInstallationId);
        var rail = new ProductionFeatureLifecycleRail(fixture.Cluster.Client, null!, null!);
        var context = new RuntimeRequestContext(
            ownerId,
            actorId,
            new SessionId("session-lifecycle-inspection-draft"),
            AuthAssurance.Oidc,
            "correlation-lifecycle-inspection-draft",
            null,
            new HashSet<string>(StringComparer.Ordinal));

        var inspections = (await rail.InspectAsync(context)).Installations;
        var inspection = Assert.Single(inspections, candidate => candidate.Authority.ActorId == actorId);
        var foreign = Assert.Single(inspections, candidate => candidate.Authority.ActorId == foreignActorId);

        Assert.Equal(installed.DraftId, inspection.Draft?.DraftId);
        Assert.Equal(installed.Goal, inspection.Draft?.Goal);
        Assert.Equal(installationId, inspection.Draft?.InstallationId);
        Assert.Equal(release, inspection.Draft?.Verification?.Release);
        Assert.Null(foreign.Runtime);
        Assert.Null(foreign.Draft);
        Assert.Single(await new DigitalBrainQueryService(rail).ListRunsAsync(context));
        Assert.Empty(await new DigitalBrainQueryService(rail).ListRunsAsync(
            context with { OwnerId = new BrainOwnerId("owner-lifecycle-inspection-other") }));
    }
}
