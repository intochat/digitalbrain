using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureDraftAuthoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReadDraft_is_local_to_the_owner_FeatureHub_state()
    {
        var ownerOne = Create("owner-1");
        var ownerTwo = Create("owner-2");

        Assert.Equal(ownerOne.Draft, FeatureDraftAuthoringTransitions.ReadDraft(ownerOne.State, ownerOne.Draft.DraftId));
        Assert.Null(FeatureDraftAuthoringTransitions.ReadDraft(ownerTwo.State, ownerOne.Draft.DraftId));
        Assert.NotEqual(ownerOne.Draft.DraftId, ownerTwo.Draft.DraftId);
    }

    [Fact]
    public void ReviseFeatureBehavior_accepts_a_bounded_Behavior_and_advances_the_Draft_Revision()
    {
        var created = Create();
        var behavior = Behavior("revised");

        var revised = FeatureDraftAuthoringTransitions.ReviseBehavior(
            created.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, behavior, 0, "behavior-1", Now.AddMinutes(1)));

        Assert.Equal(behavior, revised.Draft.Behavior);
        Assert.Equal(1, revised.Draft.Revision);
        Assert.Equal(Now.AddMinutes(1), revised.Draft.UpdatedAt);
    }

    [Fact]
    public void ReviseFeatureBehavior_rejects_empty_or_unbounded_Behavior()
    {
        var created = Create();
        var empty = new FeatureBehavior([]);
        var unbounded = new FeatureBehavior(Enumerable.Range(0, 33)
            .Select(index => Scenario($"scenario-{index}"))
            .ToArray());

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.ReviseBehavior(
            created.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, empty, 0, "behavior-empty", Now.AddMinutes(1))));
        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.ReviseBehavior(
            created.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, unbounded, 0, "behavior-unbounded", Now.AddMinutes(1))));
    }

    [Fact]
    public void ReviseFeatureBehavior_rejects_duplicate_identifiers_and_field_or_total_utf8_bounds()
    {
        var created = Create();
        var duplicate = new FeatureBehavior([Scenario("duplicate"), Scenario("duplicate")]);
        var longIdentifier = new FeatureBehavior([
            Scenario(new string('i', FeatureLimits.DraftScenarioIdCharacters + 1))
        ]);
        var longName = new FeatureBehavior([
            Scenario("long-name") with { Name = new string('n', FeatureLimits.DraftScenarioNameCharacters + 1) }
        ]);
        var longStep = new FeatureBehavior([
            Scenario("long-step") with { Given = new string('g', FeatureLimits.DraftScenarioStepCharacters + 1) }
        ]);
        var excessiveTotal = new FeatureBehavior(Enumerable.Range(0, 6)
            .Select(index => Scenario($"total-{index}") with
            {
                Given = new string('g', FeatureLimits.DraftScenarioStepCharacters),
                When = new string('w', FeatureLimits.DraftScenarioStepCharacters),
                Then = new string('t', FeatureLimits.DraftScenarioStepCharacters)
            })
            .ToArray());

        Assert.Throws<ArgumentException>(() => ReviseBehavior(created, duplicate, "duplicate"));
        Assert.Throws<ArgumentException>(() => ReviseBehavior(created, longIdentifier, "long-identifier"));
        Assert.Throws<ArgumentException>(() => ReviseBehavior(created, longName, "long-name"));
        Assert.Throws<ArgumentException>(() => ReviseBehavior(created, longStep, "long-step"));
        Assert.Throws<ArgumentException>(() => ReviseBehavior(created, excessiveTotal, "total"));
    }

    [Fact]
    public void ReviseFeatureSource_accepts_a_bounded_Source_Snapshot_with_both_entry_projects()
    {
        var created = Create();
        var source = Source();

        var revised = FeatureDraftAuthoringTransitions.ReviseSource(
            created.State,
            new ReviseFeatureSource(created.Draft.DraftId, source, 0, "source-1", Now.AddMinutes(1)));

        Assert.Equal(source, revised.Draft.Source);
        Assert.Equal(1, revised.Draft.Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviseFeatureSource_requires_each_declared_project_path_to_exactly_match_a_file_path(bool implementationProject)
    {
        var created = Create();
        var source = Source();
        source = implementationProject
            ? source with { ImplementationProjectPath = "src/feature/Feature.csproj" }
            : source with { ScenarioProjectPath = "tests/feature.Scenarios/Feature.Scenarios.csproj" };

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
            created.State,
            new ReviseFeatureSource(created.Draft.DraftId, source, 0, "source-project-case", Now.AddMinutes(1))));
    }

    [Theory]
    [InlineData("/src/Feature/Feature.csproj")]
    [InlineData("C:/src/Feature/Feature.csproj")]
    [InlineData("../Feature/Feature.csproj")]
    [InlineData("src\\Feature\\Feature.csproj")]
    [InlineData(" src/Feature/Feature.csproj")]
    [InlineData("src/Feature /Feature.csproj")]
    [InlineData("src/\tFeature/Feature.csproj")]
    [InlineData("src/Feature:stream/Feature.csproj")]
    [InlineData("src/Feature?/Feature.csproj")]
    [InlineData("src/CON/Feature.csproj")]
    public void ReviseFeatureSource_rejects_rooted_traversing_or_noncanonical_paths(string invalidPath)
    {
        var created = Create();
        var source = new FeatureSourceSnapshot(
            invalidPath,
            "tests/Feature.Scenarios/Feature.Scenarios.csproj",
            [
                new FeatureSourceFile(invalidPath, "implementation"),
                new FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "scenarios")
            ]);

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
            created.State,
            new ReviseFeatureSource(created.Draft.DraftId, source, 0, "source-invalid", Now.AddMinutes(1))));
    }

    [Fact]
    public void ReviseFeatureSource_rejects_a_Source_Snapshot_missing_an_entry_project()
    {
        var created = Create();
        var source = new FeatureSourceSnapshot(
            "src/Feature/Feature.csproj",
            "tests/Feature.Scenarios/Feature.Scenarios.csproj",
            [new FeatureSourceFile("src/Feature/Feature.csproj", "implementation")]);

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
            created.State,
            new ReviseFeatureSource(created.Draft.DraftId, source, 0, "source-missing", Now.AddMinutes(1))));
    }

    [Fact]
    public void ReviseFeatureSource_rejects_file_count_per_file_total_and_duplicate_path_bounds()
    {
        var created = Create();
        var implementationProject = "src/Feature/Feature.csproj";
        var scenarioProject = "tests/Feature.Scenarios/Feature.Scenarios.csproj";
        var tooManyFiles = new FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            [
                new FeatureSourceFile(implementationProject, "implementation"),
                new FeatureSourceFile(scenarioProject, "scenarios"),
                .. Enumerable.Range(0, FeatureLimits.DraftSourceFiles - 1)
                    .Select(index => new FeatureSourceFile($"src/Feature/File{index}.cs", "source"))
            ]);
        var tooLargeFile = new FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            [
                new FeatureSourceFile(implementationProject, "implementation"),
                new FeatureSourceFile(scenarioProject, "scenarios"),
                new FeatureSourceFile("src/Feature/Large.cs", new string('x', FeatureLimits.DraftSourceFileUtf8Bytes + 1))
            ]);
        var largeContent = new string('x', 1_000_000);
        var tooLargeTotal = new FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            [
                new FeatureSourceFile(implementationProject, largeContent),
                new FeatureSourceFile(scenarioProject, largeContent),
                new FeatureSourceFile("src/Feature/One.cs", largeContent),
                new FeatureSourceFile("src/Feature/Two.cs", largeContent),
                new FeatureSourceFile("src/Feature/Three.cs", largeContent)
            ]);
        var duplicatePath = new FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            [
                new FeatureSourceFile(implementationProject, "implementation"),
                new FeatureSourceFile(scenarioProject, "scenarios"),
                new FeatureSourceFile("SRC/FEATURE/FEATURE.CSPROJ", "duplicate")
            ]);

        Assert.Throws<ArgumentException>(() => ReviseSource(created, tooManyFiles, "file-count"));
        Assert.Throws<ArgumentException>(() => ReviseSource(created, tooLargeFile, "file-size"));
        Assert.Throws<ArgumentException>(() => ReviseSource(created, tooLargeTotal, "total-size"));
        Assert.Throws<ArgumentException>(() => ReviseSource(created, duplicatePath, "duplicate-path"));
    }

    [Fact]
    public void Authoring_rejects_a_stale_Draft_Revision()
    {
        var created = Create();
        var first = FeatureDraftAuthoringTransitions.ReviseBehavior(
            created.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, Behavior("first"), 0, "behavior-1", Now.AddMinutes(1)));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
            first.State,
            new ReviseFeatureSource(created.Draft.DraftId, Source(), 0, "source-stale", Now.AddMinutes(2))));
    }

    [Fact]
    public void Replaying_the_same_authoring_command_returns_its_prior_result_after_later_mutations()
    {
        var created = Create();
        var command = new ReviseFeatureBehavior(
            created.Draft.DraftId,
            Behavior("first"),
            0,
            "behavior-replay",
            Now.AddMinutes(1));
        var first = FeatureDraftAuthoringTransitions.ReviseBehavior(created.State, command);
        var later = FeatureDraftAuthoringTransitions.ReviseSource(
            first.State,
            new ReviseFeatureSource(created.Draft.DraftId, Source(), 1, "source-later", Now.AddMinutes(2)));

        var replay = FeatureDraftAuthoringTransitions.ReviseBehavior(later.State, command);

        Assert.Equal(first.Draft, replay.Draft);
        Assert.Same(later.State, replay.State);
        Assert.Equal(2, FeatureDraftAuthoringTransitions.ReadDraft(later.State, created.Draft.DraftId)?.Revision);
    }

    [Fact]
    public void Reusing_an_idempotency_identifier_with_a_different_payload_is_rejected()
    {
        var created = Create();
        var first = FeatureDraftAuthoringTransitions.ReviseBehavior(
            created.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, Behavior("first"), 0, "behavior-conflict", Now.AddMinutes(1)));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ReviseBehavior(
            first.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, Behavior("different"), 0, "behavior-conflict", Now.AddMinutes(1))));
    }

    [Fact]
    public void Invalid_Source_is_rejected_before_idempotency_conflict_evaluation()
    {
        var created = Create();
        var first = FeatureDraftAuthoringTransitions.ReviseSource(
            created.State,
            new ReviseFeatureSource(created.Draft.DraftId, Source(), 0, "source-conflict", Now.AddMinutes(1)));
        var files = Enumerable.Range(0, FeatureLimits.DraftSourceFiles + 1)
            .Select(index => new FeatureSourceFile($"src/Feature/File{index}.cs", "source"))
            .ToArray();
        var invalid = new FeatureSourceSnapshot(files[0].Path, files[1].Path, files);

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
            first.State,
            new ReviseFeatureSource(created.Draft.DraftId, invalid, 1, "source-conflict", Now.AddMinutes(2))));
    }

    [Fact]
    public void Replay_ledger_is_bounded_by_payload_bytes_and_preserves_recent_results()
    {
        var created = Create();
        var first = FeatureDraftAuthoringTransitions.ReviseSource(
            created.State,
            new ReviseFeatureSource(created.Draft.DraftId, LargeSource('a'), 0, "large-source-1", Now.AddMinutes(1)));
        var second = FeatureDraftAuthoringTransitions.ReviseSource(
            first.State,
            new ReviseFeatureSource(created.Draft.DraftId, LargeSource('b'), 1, "large-source-2", Now.AddMinutes(2)));
        var recentCommand = new ReviseFeatureSource(
            created.Draft.DraftId,
            LargeSource('c'),
            2,
            "large-source-3",
            Now.AddMinutes(3));
        var third = FeatureDraftAuthoringTransitions.ReviseSource(second.State, recentCommand);

        var replays = third.State.DraftReplays ?? [];
        Assert.True(replays.Sum(replay => replay.Utf8Bytes) <= FeatureLimits.DraftReplayUtf8Bytes);
        Assert.DoesNotContain(replays, replay => replay.IdempotencyId == "large-source-1");
        var replayed = FeatureDraftAuthoringTransitions.ReviseSource(third.State, recentCommand);
        Assert.Equal(third.Draft, replayed.Draft);
        Assert.Same(third.State, replayed.State);
    }

    [Fact]
    public void Owner_live_Draft_budget_accepts_the_boundary_and_rejects_creation_or_update_above_it()
    {
        var (drafts, expandableIndex, sourceBytes) = DraftsAtOwnerBudget();
        var state = FeatureHubState.Empty with { Drafts = drafts };

        Assert.Equal(FeatureLimits.DraftOwnerUtf8Bytes, FeatureDraftAuthoringTransitions.OwnerDraftUtf8Bytes(drafts));
        FeatureDraftAuthoringTransitions.DemandOwnerDraftBudget(drafts);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureHubTransitions.CreateDraft(
            state,
            "owner-budget",
            new CreateFeatureDraft("operation-over-budget", "Create another Feature", Now, "conversation-budget")));
        Assert.Throws<FeatureLimitExceededException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
            state,
            new ReviseFeatureSource(
                drafts[expandableIndex].DraftId,
                SizedSource(sourceBytes + 1),
                0,
                "source-over-budget",
                Now.AddMinutes(1))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Revising_Behavior_or_Source_invalidates_Verification(bool reviseBehavior)
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-1"));

        var revised = reviseBehavior
            ? FeatureDraftAuthoringTransitions.ReviseBehavior(
                verified.State,
                new ReviseFeatureBehavior(created.Draft.DraftId, Behavior("after-verification"), 1, "behavior-after-verification", Now.AddMinutes(2)))
            : FeatureDraftAuthoringTransitions.ReviseSource(
                verified.State,
                new ReviseFeatureSource(created.Draft.DraftId, Source(), 1, "source-after-verification", Now.AddMinutes(2)));

        Assert.Null(revised.Draft.Verification);
        Assert.Equal(2, revised.Draft.Revision);
    }

    [Fact]
    public void MarkFeatureDraftInstalled_binds_the_exact_verified_release_and_prevents_later_authoring()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-1"));
        var installationId = new FeatureInstallationId("installation-1");

        var installed = FeatureDraftAuthoringTransitions.MarkInstalled(
            verified.State,
            new MarkFeatureDraftInstalled(
                created.Draft.DraftId,
                installationId,
                verification.Release,
                1,
                "installed-1",
                Now.AddMinutes(2)));

        Assert.Equal("installed", installed.Draft.Status);
        Assert.Equal(installationId, installed.Draft.InstallationId);
        Assert.Equal(2, installed.Draft.Revision);
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ReviseBehavior(
            installed.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, Behavior("too-late"), 2, "behavior-too-late", Now.AddMinutes(3))));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.RecordVerification(
            installed.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification with { VerifiedAt = Now.AddMinutes(3) }, 2, "verification-too-late")));
    }

    [Fact]
    public void MarkFeatureDraftInstalled_rejects_a_release_other_than_the_verified_release()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-1"));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.MarkInstalled(
            verified.State,
            new MarkFeatureDraftInstalled(
                created.Draft.DraftId,
                new FeatureInstallationId("installation-1"),
                new ReleaseDigest(new string('b', 64)),
                1,
                "installed-wrong-release",
                Now.AddMinutes(2))));
    }

    [Fact]
    public void RecordFeatureVerification_rejects_a_default_release_with_a_controlled_validation_error()
    {
        var created = Create();

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                new FeatureVerification(default, 1, 1, 0, 0, Now.AddMinutes(1)),
                0,
                "verification-default-release")));
    }

    private static FeatureCreateDraftTransition Create(string owner = "owner-1") =>
        FeatureHubTransitions.CreateDraft(
            FeatureHubState.Empty,
            owner,
            new CreateFeatureDraft("operation-1", "Create a useful Feature", Now, "conversation-1"));

    private static FeatureBehavior Behavior(string suffix) => new([Scenario($"scenario-{suffix}")]);

    private static FeatureDraftAuthoringTransition ReviseBehavior(
        FeatureCreateDraftTransition created,
        FeatureBehavior behavior,
        string idempotencyId) =>
        FeatureDraftAuthoringTransitions.ReviseBehavior(
            created.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, behavior, 0, idempotencyId, Now.AddMinutes(1)));

    private static FeatureDraftAuthoringTransition ReviseSource(
        FeatureCreateDraftTransition created,
        FeatureSourceSnapshot source,
        string idempotencyId) =>
        FeatureDraftAuthoringTransitions.ReviseSource(
            created.State,
            new ReviseFeatureSource(created.Draft.DraftId, source, 0, idempotencyId, Now.AddMinutes(1)));

    private static FeatureScenario Scenario(string id) => new(
        id,
        "Create an outcome",
        "the user has a request",
        "the Feature runs",
        "the outcome is available");

    private static FeatureSourceSnapshot Source() => new(
        "src/Feature/Feature.csproj",
        "tests/Feature.Scenarios/Feature.Scenarios.csproj",
        [
            new FeatureSourceFile("src/Feature/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile("src/Feature/Feature.cs", "namespace RuntimeAuthored; public sealed class Feature;"),
            new FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile("tests/Feature.Scenarios/Feature.feature", "Feature: Runtime authored")
        ]);

    private static FeatureSourceSnapshot LargeSource(char content) => new(
        "src/Feature/Feature.csproj",
        "tests/Feature.Scenarios/Feature.Scenarios.csproj",
        [
            new FeatureSourceFile("src/Feature/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile("src/Feature/LargeOne.cs", new string(content, 900_000)),
            new FeatureSourceFile("src/Feature/LargeTwo.cs", new string(content, 900_000)),
            new FeatureSourceFile("src/Feature/LargeThree.cs", new string(content, 900_000)),
            new FeatureSourceFile("src/Feature/LargeFour.cs", new string(content, 900_000)),
            new FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
        ]);

    private static (FeatureDraft[] Drafts, int ExpandableIndex, int SourceBytes) DraftsAtOwnerBudget()
    {
        var state = FeatureHubState.Empty;
        for (var index = 0; index < 5; index++)
        {
            state = FeatureHubTransitions.CreateDraft(
                state,
                "owner-budget",
                new CreateFeatureDraft($"operation-{index}", "Create a useful Feature", Now, $"conversation-{index}")).State;
        }
        var drafts = (state.Drafts ?? []).Select(draft => WithSource(draft, SizedSource(0))).ToArray();
        var remaining = checked(FeatureLimits.DraftOwnerUtf8Bytes - (int)FeatureDraftAuthoringTransitions.OwnerDraftUtf8Bytes(drafts));
        var allocations = new int[drafts.Length];
        for (var index = 0; index < allocations.Length && remaining > 0; index++)
        {
            allocations[index] = Math.Min(remaining, FeatureLimits.DraftSourceUtf8Bytes);
            remaining -= allocations[index];
            drafts[index] = WithSource(drafts[index], SizedSource(allocations[index]));
        }
        Assert.Equal(0, remaining);
        var expandableIndex = Array.FindIndex(allocations, allocation => allocation < FeatureLimits.DraftSourceUtf8Bytes);
        Assert.True(expandableIndex >= 0);
        return (drafts, expandableIndex, allocations[expandableIndex]);
    }

    private static FeatureDraft WithSource(FeatureDraft draft, FeatureSourceSnapshot source) => new(
        draft.DraftId,
        draft.OriginatingRequest,
        draft.Goal,
        draft.Status,
        draft.Behavior,
        source,
        draft.Verification,
        draft.InstallationId,
        draft.Revision,
        draft.CreatedAt,
        draft.UpdatedAt);

    private static FeatureSourceSnapshot SizedSource(int contentBytes)
    {
        const string implementationProject = "src/Budget/Budget.csproj";
        const string scenarioProject = "tests/Budget.Scenarios/Budget.Scenarios.csproj";
        var files = new List<FeatureSourceFile>
        {
            new(implementationProject, string.Empty),
            new(scenarioProject, string.Empty)
        };
        var remaining = contentBytes;
        for (var index = 0; index < 4; index++)
        {
            var length = Math.Min(remaining, FeatureLimits.DraftSourceFileUtf8Bytes);
            files.Add(new FeatureSourceFile($"src/Budget/Padding{index}.cs", new string((char)('a' + index), length)));
            remaining -= length;
        }
        Assert.Equal(0, remaining);
        return new FeatureSourceSnapshot(implementationProject, scenarioProject, files.ToArray());
    }

    private static FeatureVerification Verification() => new(
        new ReleaseDigest(new string('a', 64)),
        1,
        1,
        0,
        0,
        Now.AddMinutes(1));
}
