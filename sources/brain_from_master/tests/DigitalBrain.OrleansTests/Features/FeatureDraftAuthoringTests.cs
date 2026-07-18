using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureDraftAuthoringTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly string InitialSourceReference = SourceReference(Create().Draft.Source);

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

    [Theory]
    [InlineData("src/COM¹/Feature.csproj")]
    [InlineData("src/com¹.txt/Feature.csproj")]
    [InlineData("src/COM²/Feature.csproj")]
    [InlineData("src/cOm².json/Feature.csproj")]
    [InlineData("src/COM³/Feature.csproj")]
    [InlineData("src/Com³.cs/Feature.csproj")]
    [InlineData("src/LPT¹/Feature.csproj")]
    [InlineData("src/lpt¹.txt/Feature.csproj")]
    [InlineData("src/LPT²/Feature.csproj")]
    [InlineData("src/lPt².json/Feature.csproj")]
    [InlineData("src/LPT³/Feature.csproj")]
    [InlineData("src/Lpt³.cs/Feature.csproj")]
    public void ReviseFeatureSource_rejects_Windows_reserved_device_aliases(string invalidPath)
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
            new ReviseFeatureSource(created.Draft.DraftId, source, 0, "source-reserved-device", Now.AddMinutes(1))));
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
    public void Behavior_lost_response_replay_ignores_a_later_server_timestamp()
    {
        var created = Create();
        var command = new ReviseFeatureBehavior(
            created.Draft.DraftId,
            Behavior("server-time"),
            0,
            "behavior-server-time",
            Now.AddMinutes(1));
        var first = FeatureDraftAuthoringTransitions.ReviseBehavior(created.State, command);

        var replay = FeatureDraftAuthoringTransitions.ReviseBehavior(
            first.State,
            command with { RevisedAt = Now.AddMinutes(2) });

        Assert.Equal(first.Draft, replay.Draft);
        Assert.Same(first.State, replay.State);
        Assert.Equal(Now.AddMinutes(1), replay.Draft.UpdatedAt);
    }

    [Fact]
    public void Source_lost_response_replay_ignores_a_later_server_timestamp()
    {
        var created = Create();
        var command = new ReviseFeatureSource(
            created.Draft.DraftId,
            Source(),
            0,
            "source-server-time",
            Now.AddMinutes(1));
        var first = FeatureDraftAuthoringTransitions.ReviseSource(created.State, command);

        var replay = FeatureDraftAuthoringTransitions.ReviseSource(
            first.State,
            command with { RevisedAt = Now.AddMinutes(2) });

        Assert.Equal(first.Draft, replay.Draft);
        Assert.Same(first.State, replay.State);
        Assert.Equal(Now.AddMinutes(1), replay.Draft.UpdatedAt);
    }

    [Fact]
    public void Suggested_change_lost_response_replay_ignores_a_later_server_timestamp()
    {
        var created = Create();
        var patch = new FeatureDraftPatch(
            "patch-server-time",
            created.Draft.DraftId,
            0,
            "Replace the authored feature",
            Behavior("patch-server-time"),
            Source());
        var command = new AcceptSuggestedChange(patch, 0, "accept-server-time", Now.AddMinutes(1));
        var first = FeatureDraftAuthoringTransitions.AcceptSuggestedChange(created.State, command);

        var replay = FeatureDraftAuthoringTransitions.AcceptSuggestedChange(
            first.State,
            command with { AcceptedAt = Now.AddMinutes(2) });

        Assert.Equal(first.Draft, replay.Draft);
        Assert.Same(first.State, replay.State);
        Assert.Equal(Now.AddMinutes(1), replay.Draft.UpdatedAt);
    }

    [Fact]
    public void Legacy_timestamp_bound_replay_digest_remains_compatible()
    {
        var created = Create();
        var original = new ReviseFeatureBehavior(
            created.Draft.DraftId,
            Behavior("legacy-replay"),
            0,
            "legacy-behavior-replay",
            Now.AddMinutes(1));
        var first = FeatureDraftAuthoringTransitions.ReviseBehavior(created.State, original);
        var legacyDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(original)));
        var legacyState = first.State with
        {
            DraftReplays = first.State.DraftReplays!
                .Select(replay => replay with { PayloadDigest = legacyDigest })
                .ToArray()
        };

        var replayed = FeatureDraftAuthoringTransitions.ReviseBehavior(
            legacyState,
            original with { RevisedAt = Now.AddMinutes(2) });

        Assert.Equal(first.Draft, replayed.Draft);
        Assert.Same(legacyState, replayed.State);
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
    public void Replay_footprint_accounts_for_every_verification_evidence_string()
    {
        var created = Create();
        var evidence = VerificationEvidence();
        var expandedEvidence = evidence with
        {
            Scenarios =
            [
                evidence.Scenarios[0] with
                {
                    ScenarioId = new string('i', FeatureLimits.DraftVerificationScenarioIdCharacters),
                    Name = new string('n', FeatureLimits.DraftVerificationScenarioNameCharacters),
                    SafeFailure = new string('f', FeatureLimits.DraftVerificationSafeFailureCharacters)
                }
            ],
            Artifacts =
            [
                evidence.Artifacts[0] with
                {
                    Name = new string('a', FeatureLimits.DraftVerificationArtifactNameCharacters),
                    MediaType = new string('m', FeatureLimits.DraftVerificationArtifactMediaTypeCharacters)
                }
            ]
        };
        var baseline = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                Verification(evidence),
                0,
                "verification-footprint"));
        var expanded = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                Verification(expandedEvidence),
                0,
                "verification-footprint"));

        var baselineBytes = Assert.Single(baseline.State.DraftReplays ?? []).Utf8Bytes;
        var expandedBytes = Assert.Single(expanded.State.DraftReplays ?? []).Utf8Bytes;
        Assert.Equal(EvidenceStringsUtf8(expandedEvidence) - EvidenceStringsUtf8(evidence), expandedBytes - baselineBytes);
    }

    [Fact]
    public void Replay_ledger_evicts_verification_evidence_above_the_byte_budget_and_preserves_the_recent_result()
    {
        var created = Create();
        var state = created.State;
        RecordFeatureVerification? recentCommand = null;
        for (var index = 0; index < 6; index++)
        {
            recentCommand = new RecordFeatureVerification(
                created.Draft.DraftId,
                Verification(LargeVerificationEvidence((char)('a' + index), 1_800)),
                index,
                $"large-verification-{index}");
            state = FeatureDraftAuthoringTransitions.RecordVerification(state, recentCommand).State;
        }

        var replays = state.DraftReplays ?? [];
        Assert.True(replays.Sum(replay => (long)replay.Utf8Bytes) <= FeatureLimits.DraftReplayUtf8Bytes);
        Assert.DoesNotContain(replays, replay => replay.IdempotencyId == "large-verification-0");
        Assert.Contains(replays, replay => replay.IdempotencyId == "large-verification-5");
        var replayed = FeatureDraftAuthoringTransitions.RecordVerification(state, recentCommand!);
        Assert.Same(state, replayed.State);
    }

    [Fact]
    public void RecordFeatureVerification_rejects_multibyte_evidence_above_the_aggregate_byte_budget()
    {
        var created = Create();
        var command = new RecordFeatureVerification(
            created.Draft.DraftId,
            Verification(LargeVerificationEvidence('\u0800', 4_096)),
            0,
            "multibyte-verification");

        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureDraftAuthoringTransitions.RecordVerification(created.State, command));
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
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            1,
            installationId,
            verification.Release,
            [],
            ["manual"],
            "decision-installed-1",
            "installed-1");
        var reservation = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            command,
            new ActorId("actor-installation"));
        var published = ConfirmPublication(reservation.State, command);

        var installed = FeatureDraftAuthoringTransitions.MarkInstalled(
            published,
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
        var installationId = new FeatureInstallationId("installation-1");
        var reservation = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            new InstallFeatureVersion(
                created.Draft.DraftId,
                1,
                installationId,
                verification.Release,
                [],
                ["manual"],
                "decision-installed-wrong-release",
                "installed-wrong-release"),
            new ActorId("actor-installation"));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.MarkInstalled(
            reservation.State,
            new MarkFeatureDraftInstalled(
                created.Draft.DraftId,
                installationId,
                new ReleaseDigest(new string('b', 64)),
                1,
                "installed-wrong-release",
                Now.AddMinutes(2))));
    }

    [Fact]
    public void Installation_reservation_is_exact_replayable_and_blocks_new_authoring()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-reservation"));
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            new FeatureInstallationId("installation-reservation"),
            verification.Release,
            [
                new FeatureGrantSpec("capability.z", 1, null, "{\"allowedToolIds\":[\"capability.z\"]}"),
                new FeatureGrantSpec("capability.a", 1, null, "{\"allowedToolIds\":[\"capability.a\"]}")
            ],
            ["z-event", "a-event"],
            "decision-reservation",
            "install-reservation");

        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            command,
            new ActorId("actor-installation"));
        var replayed = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            reserved.State,
            command with
            {
                Grants = command.Grants.Reverse().ToArray(),
                Subscriptions = command.Subscriptions.Reverse().ToArray()
            },
            new ActorId("actor-installation"));

        Assert.Same(reserved.State, replayed.State);
        Assert.Equal(reserved.Reservation, replayed.Reservation);
        Assert.Equal(
            ["capability.a", "capability.z"],
            Assert.IsType<FeatureGrantSpec[]>(reserved.Reservation.Grants).Select(grant => grant.CapabilityId));
        Assert.Equal(["a-event", "z-event"], Assert.IsType<string[]>(reserved.Reservation.Subscriptions));
        var changedPlan = reserved.State with
        {
            DraftInstallationReservations =
            [
                reserved.Reservation with { Subscriptions = ["other-event"] }
            ]
        };
        Assert.False(FeatureStateEquality.Same(reserved.State, changedPlan));
        var legacy = reserved.State with
        {
            DraftInstallationReservations =
            [
                reserved.Reservation with { Grants = null, Subscriptions = null }
            ]
        };
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            legacy,
            command,
            new ActorId("actor-installation")));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            reserved.State,
            command with { Subscriptions = ["conversation.completed"] },
            new ActorId("actor-installation")));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            reserved.State,
            command,
            new ActorId("actor-other")));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ReviseBehavior(
            reserved.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, Behavior("reserved"), verified.Draft.Revision, "behavior-reserved", Now.AddMinutes(2))));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
            reserved.State,
            new ReviseFeatureSource(created.Draft.DraftId, Source(), verified.Draft.Revision, "source-reserved", Now.AddMinutes(2))));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.AcceptSuggestedChange(
            reserved.State,
            new AcceptSuggestedChange(
                new FeatureDraftPatch("patch-reserved", created.Draft.DraftId, verified.Draft.Revision, "Reserved", Behavior("reserved"), Source()),
                verified.Draft.Revision,
                "accept-reserved",
                Now.AddMinutes(2))));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.RecordVerification(
            reserved.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification with { VerifiedAt = Now.AddMinutes(2) }, verified.Draft.Revision, "verification-reserved")));
    }

    [Fact]
    public void Installation_recovery_ledger_enforces_one_aggregate_UTF8_budget_across_reservations_and_resets()
    {
        var grant = new FeatureGrantSpec("capability.ledger", 1, null, string.Empty);
        var reservation = new FeatureDraftInstallationReservation(
            new FeatureDraftId("draft-ledger"),
            1,
            new FeatureInstallationId("installation-ledger"),
            new ReleaseDigest(new string('a', 64)),
            "install-ledger",
            new string('b', 64),
            new string('c', 64),
            "decision-ledger",
            new ActorId("actor-ledger"),
            [grant],
            ["manual"]);
        var baseBytes = FeatureDraftAuthoringTransitions.InstallationLedgerUtf8Bytes([reservation], []);
        var paddingLength = FeatureLimits.DraftInstallationLedgerUtf8Bytes - baseBytes;
        var exact = reservation with
        {
            Grants = [grant with { ConstraintsJson = new string('x', paddingLength) }]
        };
        var reset = new FeatureDraftInstallationResetState(
            reservation.DraftId,
            "reset-ledger",
            reservation.ActorId,
            Now,
            reservation.InstallationId,
            reservation.Release,
            reservation.CommandDigest,
            true,
            1,
            new string('d', 64),
            new string('e', 64));

        Assert.True(paddingLength > 0);
        Assert.Equal(
            FeatureLimits.DraftInstallationLedgerUtf8Bytes,
            FeatureDraftAuthoringTransitions.InstallationLedgerUtf8Bytes([exact], []));
        FeatureDraftAuthoringTransitions.DemandInstallationLedgerBudget([exact], []);
        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureDraftAuthoringTransitions.DemandInstallationLedgerBudget([exact], [reset]));
        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureDraftAuthoringTransitions.DemandInstallationLedgerBudget(
                [exact with { Grants = [grant with { ConstraintsJson = new string('x', paddingLength + 1) }] }],
                []));
    }

    [Fact]
    public void Oversized_seeded_recovery_ledger_blocks_forward_replay_but_keeps_exact_reset_recovery_available()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-oversized-ledger"));
        var actor = new ActorId("actor-oversized-ledger");
        var grant = new FeatureGrantSpec(
            "capability.oversized-ledger",
            1,
            null,
            "{\"allowedToolIds\":[\"capability.oversized-ledger\"]}");
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            new FeatureInstallationId("installation-oversized-ledger-reset"),
            verification.Release,
            [grant],
            ["manual"],
            "decision-oversized-ledger-reset",
            "install-oversized-ledger-reset");
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(verified.State, command, actor);
        var forwardCommand = command with
        {
            DraftId = new FeatureDraftId("draft-oversized-ledger-forward"),
            InstallationId = new FeatureInstallationId("installation-oversized-ledger-forward"),
            DecisionId = "decision-oversized-ledger-forward",
            IdempotencyId = "install-oversized-ledger-forward"
        };
        var forwardReservation = reserved.Reservation with
        {
            DraftId = forwardCommand.DraftId,
            InstallationId = forwardCommand.InstallationId,
            DecisionId = forwardCommand.DecisionId,
            IdempotencyId = forwardCommand.IdempotencyId,
            CommandDigest = FeatureInstallationReservationDigests.Command(forwardCommand),
            AccessDigest = FeatureInstallationReservationDigests.Access(
                forwardCommand.InstallationId,
                forwardCommand.Release,
                forwardCommand.Grants,
                forwardCommand.Subscriptions)
        };
        var oversizedLegacyReservation = reserved.Reservation with
        {
            DraftId = new FeatureDraftId("draft-oversized-ledger-legacy"),
            InstallationId = new FeatureInstallationId("installation-oversized-ledger-legacy"),
            Grants = [grant with { ConstraintsJson = new string('x', FeatureLimits.DraftInstallationLedgerUtf8Bytes) }]
        };
        var seeded = reserved.State with
        {
            DraftInstallationReservations =
            [
                reserved.Reservation,
                forwardReservation,
                oversizedLegacyReservation
            ]
        };

        Assert.True(FeatureDraftAuthoringTransitions.InstallationLedgerUtf8Bytes(
            seeded.DraftInstallationReservations,
            seeded.DraftInstallationResets ?? []) > FeatureLimits.DraftInstallationLedgerUtf8Bytes);
        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureDraftAuthoringTransitions.AcquireInstallationReservation(seeded, forwardCommand, actor));

        var resetCommand = new ResetFeatureDraftInstallationReservation(
            command.DraftId,
            "reset-oversized-ledger",
            command);
        var reset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            seeded,
            resetCommand,
            actor,
            Now.AddMinutes(2));
        var replayedReset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            reset.State,
            resetCommand with { ReservedInstallation = null },
            actor,
            Now.AddHours(1));

        Assert.Same(reset.State, replayedReset.State);
        Assert.DoesNotContain(reset.State.DraftInstallationReservations ?? [], candidate => candidate.DraftId == command.DraftId);
        Assert.Contains(reset.State.DraftInstallationReservations ?? [], candidate => candidate.DraftId == forwardCommand.DraftId);
        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureDraftAuthoringTransitions.AcquireInstallationReservation(reset.State, forwardCommand, actor));
    }

    [Fact]
    public void Same_release_reservation_rejects_grant_only_and_subscription_only_access_changes()
    {
        var verification = Verification();
        var installationId = new FeatureInstallationId("installation-same-release-access");
        var actor = new ActorId("actor-same-release-access");
        var grant = new FeatureGrantSpec(
            "capability.same",
            1,
            null,
            "{\"allowedToolIds\":[\"capability.same\"]}");
        var authority = new FeatureInstallationAuthorityState(
            installationId,
            actor,
            verification.Release,
            null,
            new GrantRevision(1),
            [new FeatureGrantState(
                grant.CapabilityId,
                grant.CapabilityVersion,
                grant.ProviderConnectionId,
                grant.ConstraintsJson,
                grant.Provider)],
            null,
            [],
            null,
            null,
            [],
            false,
            null);
        var registration = new FeatureInstallationRegistration(
            installationId,
            verification.Release,
            ["manual"]);
        var created = FeatureHubTransitions.CreateDraft(
            FeatureHubState.Empty with
            {
                Authorities = [authority],
                Installations = [registration]
            },
            "owner-same-release-access",
            new CreateFeatureDraft(
                "operation-same-release-access",
                "Review a same-release access plan",
                Now,
                "conversation-same-release-access"));
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                verification,
                created.Draft.Revision,
                "verification-same-release-access"));
        var command = new InstallFeatureVersion(
            verified.Draft.DraftId,
            verified.Draft.Revision,
            installationId,
            verification.Release,
            [grant],
            registration.Subscriptions,
            "decision-same-release-access",
            "install-same-release-access",
            7,
            verification.Release,
            null);

        var exact = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            command,
            actor);
        var changedGrant = Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
                verified.State,
                command with
                {
                    Grants = [grant with { CapabilityVersion = 2 }],
                    IdempotencyId = "install-same-release-grant-change"
                },
                actor));
        var changedSubscription = Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
                verified.State,
                command with
                {
                    Subscriptions = ["conversation.completed"],
                    IdempotencyId = "install-same-release-subscription-change"
                },
                actor));

        Assert.NotNull(exact.Reservation.AuthorityBaseline);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, changedGrant.Reason);
        Assert.Equal(FeatureCommandRejectionReason.Precondition, changedSubscription.Reason);
    }

    [Fact]
    public void A_reservation_without_a_confirmed_active_publication_cannot_mark_the_Draft_installed()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-unpublished"));
        var installationId = new FeatureInstallationId("installation-unpublished");
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            installationId,
            verification.Release,
            [],
            ["manual"],
            "decision-unpublished",
            "install-unpublished");
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            command,
            new ActorId("actor-installation"));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.MarkInstalled(
            reserved.State,
            new MarkFeatureDraftInstalled(
                created.Draft.DraftId,
                installationId,
                verification.Release,
                verified.Draft.Revision,
                command.IdempotencyId,
                Now.AddMinutes(2))));
    }

    [Fact]
    public void A_confirmed_publication_by_another_actor_cannot_consume_the_reserved_installation()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-actor-swap"));
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            new FeatureInstallationId("installation-actor-swap"),
            verification.Release,
            [],
            ["manual"],
            "decision-actor-swap",
            "install-actor-swap");
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            command,
            new ActorId("actor-a"));
        var rejected = Assert.Throws<FeatureAuthorityRejectedException>(() => ConfirmPublication(
            reserved.State,
            command,
            new ActorId("actor-b")));
        Assert.Equal(FeatureAuthorityRejectionReason.ActorMismatch, rejected.Reason);
        Assert.Single(reserved.State.DraftInstallationReservations ?? []);
    }

    [Fact]
    public void Marking_installed_atomically_consumes_the_exact_reservation_and_replays()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-consume"));
        var installationId = new FeatureInstallationId("installation-consume");
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            installationId,
            verification.Release,
            [],
            ["manual"],
            "decision-consume",
            "install-consume");
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            verified.State,
            command,
            new ActorId("actor-installation"));
        var mark = new MarkFeatureDraftInstalled(
            created.Draft.DraftId,
            installationId,
            verification.Release,
            verified.Draft.Revision,
            command.IdempotencyId,
            Now.AddMinutes(2));
        var published = ConfirmPublication(reserved.State, command);

        var installed = FeatureDraftAuthoringTransitions.MarkInstalled(published, mark);
        var replayed = FeatureDraftAuthoringTransitions.MarkInstalled(installed.State, mark);

        Assert.Empty(installed.State.DraftInstallationReservations ?? []);
        Assert.Same(installed.State, replayed.State);
        Assert.Equal(installed.Draft, replayed.Draft);
    }

    [Fact]
    public void Resetting_an_exact_reservation_is_actor_bound_replayable_and_makes_the_Draft_editable()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-reset"));
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            new FeatureInstallationId("installation-reset"),
            verification.Release,
            [new FeatureGrantSpec("capability.one", 1, null, "{\"allowedToolIds\":[\"capability.one\"]}")],
            ["manual"],
            "decision-reset",
            "install-reset");
        var actor = new ActorId("actor-reset");
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(verified.State, command, actor);
        var resetCommand = new ResetFeatureDraftInstallationReservation(
            command.DraftId,
            "reset-reservation",
            command);

        var resetAt = Now.AddMinutes(2);
        var reset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(reserved.State, resetCommand, actor, resetAt);
        var replayed = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            reset.State,
            resetCommand with { ReservedInstallation = null },
            actor,
            Now.AddHours(1));

        Assert.Empty(reset.State.DraftInstallationReservations ?? []);
        Assert.Null(reset.Draft.Verification);
        Assert.Equal(verified.Draft.Revision + 1, reset.Draft.Revision);
        Assert.Equal(resetAt, reset.Draft.UpdatedAt);
        Assert.Same(reset.State, replayed.State);
        Assert.Equal(reset.Draft, replayed.Draft);
        Assert.True(reset.RequiresNewRuntimeDiscard);
        Assert.False(reset.RequiresRepublish);
        Assert.Throws<FeatureAuthorityRejectedException>(() => FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            reset.State,
            resetCommand with { ReservedInstallation = null },
            new ActorId("actor-other"),
            Now.AddHours(1)));
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            reset.State,
            resetCommand with { ReservedInstallation = command with { DecisionId = "different-decision" } },
            actor,
            Now.AddHours(1)));

        var revised = FeatureDraftAuthoringTransitions.ReviseBehavior(
            reset.State,
            new ReviseFeatureBehavior(
                command.DraftId,
                Behavior("after-reset"),
                reset.Draft.Revision,
                "behavior-after-reset",
                Now.AddMinutes(3)));

        Assert.Equal(reset.Draft.Revision + 1, revised.Draft.Revision);

        var reverified = FeatureDraftAuthoringTransitions.RecordVerification(
            reset.State,
            new RecordFeatureVerification(
                command.DraftId,
                verification with { VerifiedAt = Now.AddMinutes(4) },
                reset.Draft.Revision,
                "verification-after-reset"));
        var newCommand = command with
        {
            ExpectedRevision = reverified.Draft.Revision,
            IdempotencyId = "install-after-reset"
        };
        var newlyReserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            reverified.State,
            newCommand,
            actor);
        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            newlyReserved.State,
            resetCommand with { ReservedInstallation = null },
            actor,
            Now.AddMinutes(5)));
    }

    [Fact]
    public void Resetting_supersedes_the_exact_approval_and_allows_a_fresh_same_release_access_plan()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-reset-approval"));
        var oldGrant = new FeatureGrantSpec(
            "capability.one",
            1,
            new ProviderConnectionId("connection-old"),
            "{\"allowedToolIds\":[\"capability.one\"]}",
            "sandbox");
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            new FeatureInstallationId("installation-reset-approval"),
            verification.Release,
            [oldGrant],
            ["manual"],
            "decision-reset-approval",
            "install-reset-approval");
        var actor = new ActorId("actor-reset-approval");
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(verified.State, command, actor);
        var historicalApprovals = Enumerable.Range(1, 63).Select(index =>
        {
            var historicalRelease = new ReleaseDigest(index.ToString("x64"));
            return new FeatureApprovalState(
                $"historical-approval-{index:D2}",
                new FeatureInstallationId($"historical-installation-{index:D2}"),
                new FeatureReleaseMetadata(
                    historicalRelease,
                    "sha256:" + historicalRelease.Value,
                    FeatureSourceKind.RuntimeAuthored,
                    [],
                    [],
                    new FeatureSourceSnapshot(
                        $"src/history-{index:D2}/Feature.csproj",
                        $"tests/history-{index:D2}/Feature.Scenarios.csproj",
                        [new FeatureSourceFile($"src/history-{index:D2}/Feature.cs", "history")])),
                [],
                [],
                FeatureApprovalStatus.Superseded,
                $"historical-decision-{index:D2}",
                Now,
                index,
                [],
                actor);
        }).ToArray();
        reserved = reserved with
        {
            State = reserved.State with { Approvals = historicalApprovals, Revision = 1000 }
        };
        var source = Source();
        var release = new FeatureReleaseMetadata(
            command.Release,
            FeatureDraftAuthoringTransitions.SourceReference(source),
            FeatureSourceKind.RuntimeAuthored,
            [oldGrant.CapabilityId],
            [],
            source);
        var proposed = FeatureHubTransitions.Propose(
            reserved.State,
            new FeatureReleaseProposal(command.InstallationId, release, [oldGrant]),
            reserved.State.Revision,
            Now);

        var reset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            proposed,
            new ResetFeatureDraftInstallationReservation(command.DraftId, "reset-approval", command),
            actor,
            Now.AddMinutes(2));
        var newGrant = oldGrant with { ProviderConnectionId = new ProviderConnectionId("connection-new") };
        var reverified = FeatureDraftAuthoringTransitions.RecordVerification(
            reset.State,
            new RecordFeatureVerification(
                command.DraftId,
                verification with { VerifiedAt = Now.AddMinutes(3) },
                reset.Draft.Revision,
                "verification-reset-approval-fresh"));
        var freshCommand = command with
        {
            ExpectedRevision = reverified.Draft.Revision,
            Grants = [newGrant],
            DecisionId = "decision-reset-approval-fresh",
            IdempotencyId = "install-reset-approval-fresh"
        };
        var reReserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(
            reverified.State,
            freshCommand,
            actor);
        var reproposed = FeatureHubTransitions.Propose(
            reReserved.State,
            new FeatureReleaseProposal(command.InstallationId, release, [newGrant]),
            reReserved.State.Revision,
            Now.AddMinutes(4));

        Assert.Equal(FeatureLimits.ApprovalLedgerRecords, reset.State.Approvals.Length);
        Assert.All(reset.State.Approvals, approval => Assert.Equal(FeatureApprovalStatus.Superseded, approval.Status));
        Assert.All(reset.State.Approvals, approval => Assert.Null(approval.Release.Source));
        Assert.Empty(reset.State.Releases);
        Assert.Equal(FeatureLimits.ApprovalLedgerRecords, reproposed.Approvals.Length);
        Assert.DoesNotContain(reproposed.Approvals, approval => approval.ApprovalId == "historical-approval-01");
        Assert.Contains(reproposed.Approvals, approval =>
            approval.Status == FeatureApprovalStatus.Pending &&
            approval.Grants.Single().ProviderConnectionId == newGrant.ProviderConnectionId);
    }

    [Fact]
    public void Resetting_supersedes_an_exact_rejected_approval_without_erasing_its_decision_history()
    {
        var created = Create();
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-reset-rejected"));
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            new FeatureInstallationId("installation-reset-rejected"),
            verification.Release,
            [],
            ["manual"],
            "decision-reset-rejected",
            "install-reset-rejected");
        var actor = new ActorId("actor-reset-rejected");
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(verified.State, command, actor);
        var proposed = FeatureHubTransitions.Propose(
            reserved.State,
            new FeatureReleaseProposal(
                command.InstallationId,
                new FeatureReleaseMetadata(
                    command.Release,
                    "sha256:" + command.Release.Value,
                    FeatureSourceKind.RuntimeAuthored,
                    [],
                    []),
                []),
            reserved.State.Revision,
            Now);
        var pending = proposed.Approvals.Single();
        var rejected = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(pending.ApprovalId, command.Release, false, command.DecisionId, actor),
            proposed.Revision,
            Now.AddMinutes(1));

        var reset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            rejected,
            new ResetFeatureDraftInstallationReservation(command.DraftId, "reset-rejected", command),
            actor,
            Now.AddMinutes(2));
        var superseded = reset.State.Approvals.Single();

        Assert.Equal(FeatureApprovalStatus.Superseded, superseded.Status);
        Assert.Equal(command.DecisionId, superseded.DecisionId);
        Assert.Equal(Now.AddMinutes(1), superseded.DecidedAt);
    }

    [Fact]
    public void Resetting_an_update_preserves_active_authority_and_clears_only_the_exact_candidate()
    {
        var activeRelease = new ReleaseDigest(new string('b', 64));
        var candidateRelease = Verification().Release;
        var installationId = new FeatureInstallationId("installation-reset-update");
        var actor = new ActorId("actor-reset-update");
        var activeGrant = new FeatureGrantState(
            "capability.one",
            1,
            null,
            "{\"allowedToolIds\":[\"capability.one\"]}",
            null);
        var activeRegistration = new FeatureInstallationRegistration(installationId, activeRelease, ["active-event"]);
        var activeAuthority = new FeatureInstallationAuthorityState(
            installationId,
            actor,
            activeRelease,
            null,
            new GrantRevision(1),
            [activeGrant],
            null,
            [],
            null,
            null,
            [],
            true,
            "operator pause",
            7,
            null,
            null,
            null);
        var baseState = FeatureHubState.Empty with
        {
            Installations = [activeRegistration],
            Releases =
            [
                new FeatureReleaseMetadata(activeRelease, "sha256:" + activeRelease.Value, FeatureSourceKind.RuntimeAuthored, ["capability.one"], [])
            ],
            Authorities = [activeAuthority]
        };
        var created = FeatureHubTransitions.CreateDraft(
            baseState,
            "owner-reset-update",
            new CreateFeatureDraft("operation-reset-update", "Update a Feature", Now, "conversation-reset-update"));
        var verification = Verification();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(created.Draft.DraftId, verification, 0, "verification-reset-update"));
        var candidateGrant = new FeatureGrantSpec(
            "capability.one",
            1,
            null,
            "{\"allowedToolIds\":[\"capability.one\"]}");
        var command = new InstallFeatureVersion(
            created.Draft.DraftId,
            verified.Draft.Revision,
            installationId,
            candidateRelease,
            [candidateGrant],
            ["candidate-event"],
            "decision-reset-update",
            "install-reset-update",
            42,
            activeRelease,
            null);
        var reserved = FeatureDraftAuthoringTransitions.AcquireInstallationReservation(verified.State, command, actor);
        var candidate = new FeatureReleaseMetadata(
            candidateRelease,
            "sha256:" + candidateRelease.Value,
            FeatureSourceKind.RuntimeAuthored,
            [candidateGrant.CapabilityId],
            []);
        var proposed = FeatureHubTransitions.Propose(
            reserved.State,
            new FeatureReleaseProposal(installationId, candidate, [candidateGrant]),
            reserved.State.Revision,
            Now);
        var approval = proposed.Approvals.Single(approval => approval.Release.Digest == candidateRelease);
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(approval.ApprovalId, candidateRelease, true, command.DecisionId, actor),
            proposed.Revision,
            Now);
        var staged = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(installationId, candidateRelease, actor, [candidateGrant]),
            approved.Revision);

        var reset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            staged,
            new ResetFeatureDraftInstallationReservation(command.DraftId, "reset-update", command),
            actor,
            Now.AddMinutes(2));
        var preserved = Assert.Single(reset.State.Authorities);

        Assert.Equal(activeAuthority.InstallationId, preserved.InstallationId);
        Assert.Equal(activeAuthority.ActorId, preserved.ActorId);
        Assert.Equal(activeAuthority.ActiveRelease, preserved.ActiveRelease);
        Assert.Equal(activeAuthority.ActiveGrantRevision, preserved.ActiveGrantRevision);
        Assert.Equal(activeAuthority.ActiveGrants, preserved.ActiveGrants);
        Assert.Equal(activeAuthority.Paused, preserved.Paused);
        Assert.Equal(activeAuthority.PauseReason, preserved.PauseReason);
        Assert.Equal(8, preserved.PublicationFence);
        Assert.Null(preserved.PublicationReceipt);
        var preservedRegistration = Assert.Single(reset.State.Installations);
        Assert.Equal(activeRegistration.InstallationId, preservedRegistration.InstallationId);
        Assert.Equal(activeRegistration.Release, preservedRegistration.Release);
        Assert.Equal(activeRegistration.Subscriptions, preservedRegistration.Subscriptions);
        Assert.Equal(FeatureApprovalStatus.Superseded, reset.State.Approvals.Single().Status);
        Assert.False(reset.RequiresNewRuntimeDiscard);
        Assert.False(reset.RequiresRepublish);
        Assert.Equal(activeRelease, reset.PreservedActiveRelease);

        var activated = FeatureHubTransitions.Register(
            FeatureHubTransitions.Activate(staged, installationId, staged.Revision),
            new FeatureInstallationRegistration(installationId, candidateRelease, command.Subscriptions));
        var activatedReset = FeatureDraftAuthoringTransitions.ResetInstallationReservation(
            activated,
            new ResetFeatureDraftInstallationReservation(command.DraftId, "reset-activated", command),
            actor,
            Now.AddMinutes(3));
        Assert.Equal(activeRelease, Assert.Single(activatedReset.State.Authorities).ActiveRelease);
        Assert.Null(Assert.Single(activatedReset.State.Authorities).PendingRelease);
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

    [Fact]
    public void RecordFeatureVerification_rejects_missing_evidence()
    {
        var created = Create();

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                Verification(null),
                0,
                "verification-missing-evidence")));
    }

    [Fact]
    public void RecordFeatureVerification_rejects_evidence_for_another_source_snapshot()
    {
        var created = Create();
        var evidence = PassingVerificationEvidence() with { SourceReference = $"sha256:{new string('0', 64)}" };

        Assert.Throws<ArgumentException>(() => FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                Verification(evidence),
                0,
                "verification-source-mismatch")));
    }

    [Fact]
    public void ReadDraft_preserves_a_legacy_verification_without_evidence()
    {
        var created = Create();
        var legacyVerification = Verification(null);
        var legacyDraft = new FeatureDraft(
            created.Draft.DraftId,
            created.Draft.OriginatingRequest,
            created.Draft.Goal,
            created.Draft.Status,
            created.Draft.Behavior,
            created.Draft.Source,
            legacyVerification,
            created.Draft.InstallationId,
            created.Draft.Revision,
            created.Draft.CreatedAt,
            created.Draft.UpdatedAt);
        var legacyState = created.State with { Drafts = [legacyDraft] };

        var read = FeatureDraftAuthoringTransitions.ReadDraft(legacyState, legacyDraft.DraftId);

        var readVerification = Assert.IsType<FeatureVerification>(read?.Verification);
        Assert.Same(legacyVerification, readVerification);
        Assert.Null(readVerification.Evidence);
    }

    [Theory]
    [InlineData("source-reference")]
    [InlineData("total-bound")]
    [InlineData("scenario-count")]
    [InlineData("scenario-id")]
    [InlineData("scenario-name")]
    [InlineData("duplicate-scenario")]
    [InlineData("duration")]
    [InlineData("passing-failure")]
    [InlineData("failed-failure")]
    [InlineData("skipped-reason")]
    [InlineData("unknown-outcome")]
    [InlineData("null-scenarios")]
    [InlineData("null-scenario")]
    [InlineData("artifact-count")]
    [InlineData("artifact-name")]
    [InlineData("duplicate-artifact")]
    [InlineData("artifact-media-type")]
    [InlineData("artifact-size")]
    [InlineData("artifact-digest")]
    [InlineData("null-artifacts")]
    [InlineData("null-artifact")]
    public void RecordFeatureVerification_rejects_invalid_persisted_evidence(string invalidity)
    {
        var created = Create();

        Assert.ThrowsAny<ArgumentException>(() => FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                InvalidVerification(invalidity),
                0,
                "verification-invalid-evidence")));
    }

    [Fact]
    public void Incrementing_Draft_commands_reject_MaxValue_revision_as_a_typed_conflict_while_rejection_remains_a_no_write()
    {
        var created = Create();
        var draft = new FeatureDraft(
            created.Draft.DraftId,
            created.Draft.OriginatingRequest,
            created.Draft.Goal,
            created.Draft.Status,
            created.Draft.Behavior,
            created.Draft.Source,
            created.Draft.Verification,
            created.Draft.InstallationId,
            long.MaxValue,
            created.Draft.CreatedAt,
            created.Draft.UpdatedAt);
        var state = created.State with { Drafts = [draft] };
        var patch = new FeatureDraftPatch(
            "patch-max-revision",
            draft.DraftId,
            long.MaxValue,
            "Max revision",
            Behavior("max-patch"),
            Source());

        FeatureConcurrencyException[] rejected =
        [
            Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ReviseBehavior(
                state,
                new ReviseFeatureBehavior(draft.DraftId, Behavior("max-behavior"), long.MaxValue, "behavior-max", Now.AddMinutes(1)))),
            Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.ReviseSource(
                state,
                new ReviseFeatureSource(draft.DraftId, Source(), long.MaxValue, "source-max", Now.AddMinutes(1)))),
            Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.AcceptSuggestedChange(
                state,
                new AcceptSuggestedChange(patch, long.MaxValue, "accept-max", Now.AddMinutes(1)))),
            Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.RecordVerification(
                state,
                new RecordFeatureVerification(
                    draft.DraftId,
                    Verification() with { VerifiedAt = Now.AddMinutes(1) },
                    long.MaxValue,
                    "verification-max")))
        ];
        var noWrite = FeatureDraftAuthoringTransitions.RejectSuggestedChange(
            state,
            new RejectSuggestedChange(draft.DraftId, patch.PatchId, long.MaxValue, long.MaxValue));

        Assert.All(rejected, exception => Assert.Equal(FeatureCommandRejectionReason.Conflict, exception.Reason));
        Assert.Same(state, noWrite.State);
        Assert.Same(draft, noWrite.Draft);
    }

    private static FeatureCreateDraftTransition Create(string owner = "owner-1") =>
        FeatureHubTransitions.CreateDraft(
            FeatureHubState.Empty,
            owner,
            new CreateFeatureDraft("operation-1", "Create a useful Feature", Now, "conversation-1"));

    private static FeatureHubState ConfirmPublication(
        FeatureHubState state,
        InstallFeatureVersion command,
        ActorId? actorId = null)
    {
        var metadata = new FeatureReleaseMetadata(
            command.Release,
            "sha256:" + command.Release.Value,
            FeatureSourceKind.RuntimeAuthored,
            command.Grants.Select(grant => grant.CapabilityId).ToArray(),
            []);
        var proposed = FeatureHubTransitions.Propose(
            state,
            new FeatureReleaseProposal(command.InstallationId, metadata, command.Grants),
            state.Revision,
            Now);
        var approval = proposed.Approvals.Single(candidate =>
            candidate.InstallationId == command.InstallationId && candidate.Release.Digest == command.Release);
        var actor = actorId ?? new ActorId("actor-installation");
        var approved = FeatureHubTransitions.Decide(
            proposed,
            new FeatureApprovalDecision(approval.ApprovalId, command.Release, true, command.DecisionId, actor),
            proposed.Revision,
            Now);
        var staged = FeatureHubTransitions.Grant(
            approved,
            new FeatureGrantRequest(command.InstallationId, command.Release, actor, command.Grants),
            approved.Revision);
        var active = FeatureHubTransitions.Activate(staged, command.InstallationId, staged.Revision);
        var registered = FeatureHubTransitions.Register(
            active,
            new FeatureInstallationRegistration(command.InstallationId, command.Release, command.Subscriptions));
        var prepared = FeaturePublicationTransitions.Prepare(registered, command.InstallationId);
        return FeaturePublicationTransitions.Confirm(
            prepared.State,
            new FeaturePublicationReceipt(
                command.InstallationId,
                prepared.Ticket.PublicationFence,
                prepared.Ticket.AuthorityDigest,
                prepared.Ticket.AccessDigest,
                new string('f', 64))).State;
    }

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

    private static FeatureVerification Verification() => Verification(PassingVerificationEvidence());

    private static FeatureVerification Verification(FeatureVerificationEvidence? evidence) => new(
        new ReleaseDigest(new string('a', 64)),
        evidence?.Total ?? 1,
        evidence?.Passed ?? 1,
        evidence?.Failed ?? 0,
        evidence?.Skipped ?? 0,
        Now.AddMinutes(1),
        evidence);

    private static FeatureVerificationEvidence PassingVerificationEvidence() => new(
        InitialSourceReference,
        1,
        1,
        0,
        0,
        [new FeatureScenarioEvidence("scenario-verification", "Verification scenario", FeatureScenarioOutcome.Passed, null, 25)],
        [new FeatureVerificationArtifact("verification.trx", "application/xml", 1024, $"sha256:{new string('c', 64)}")]);

    private static FeatureVerificationEvidence VerificationEvidence() => new(
        InitialSourceReference,
        1,
        0,
        1,
        0,
        [new FeatureScenarioEvidence("scenario-verification", "Verification scenario", FeatureScenarioOutcome.Failed, "Scenario failed safely.", 25)],
        [new FeatureVerificationArtifact("verification.trx", "application/xml", 1024, $"sha256:{new string('c', 64)}")]);

    private static FeatureVerificationEvidence LargeVerificationEvidence(char content, int safeFailureCharacters) => new(
        InitialSourceReference,
        1024,
        0,
        1024,
        0,
        Enumerable.Range(0, 1024)
            .Select(index => new FeatureScenarioEvidence(
                $"scenario-{index}",
                $"Verification scenario {index}",
                FeatureScenarioOutcome.Failed,
                new string(content, safeFailureCharacters),
                70_000))
            .ToArray(),
        [new FeatureVerificationArtifact("verification.trx", "application/xml", 1_048_576, $"sha256:{new string('c', 64)}")]);

    private static int EvidenceStringsUtf8(FeatureVerificationEvidence evidence) =>
        Encoding.UTF8.GetByteCount(evidence.SourceReference) +
        evidence.Scenarios.Sum(scenario => Encoding.UTF8.GetByteCount(scenario.ScenarioId) +
            Encoding.UTF8.GetByteCount(scenario.Name) + Encoding.UTF8.GetByteCount(scenario.SafeFailure ?? string.Empty)) +
        evidence.Artifacts.Sum(artifact => Encoding.UTF8.GetByteCount(artifact.Name) +
            Encoding.UTF8.GetByteCount(artifact.MediaType) + Encoding.UTF8.GetByteCount(artifact.Digest));

    private static string SourceReference(FeatureSourceSnapshot source) =>
        FeatureDraftAuthoringTransitions.SourceReference(source);

    private static FeatureVerification InvalidVerification(string invalidity)
    {
        var evidence = VerificationEvidence();
        evidence = invalidity switch
        {
            "source-reference" => evidence with { SourceReference = new string('b', 64) },
            "total-bound" => evidence with
            {
                Total = 1025,
                Passed = 1025,
                Failed = 0,
                Scenarios = Enumerable.Range(0, 1025)
                    .Select(index => new FeatureScenarioEvidence($"scenario-{index}", "Scenario", FeatureScenarioOutcome.Passed, null, 0))
                    .ToArray()
            },
            "scenario-count" => evidence with { Total = 2, Failed = 2 },
            "scenario-id" => evidence with { Scenarios = [evidence.Scenarios[0] with { ScenarioId = new string('x', 257) }] },
            "scenario-name" => evidence with { Scenarios = [evidence.Scenarios[0] with { Name = new string('x', 513) }] },
            "duplicate-scenario" => evidence with
            {
                Total = 2,
                Failed = 2,
                Scenarios = [evidence.Scenarios[0], evidence.Scenarios[0] with { Name = "Duplicate scenario" }]
            },
            "duration" => evidence with { Scenarios = [evidence.Scenarios[0] with { DurationMilliseconds = 70_001 }] },
            "passing-failure" => evidence with
            {
                Passed = 1,
                Failed = 0,
                Scenarios = [evidence.Scenarios[0] with { Outcome = FeatureScenarioOutcome.Passed }]
            },
            "failed-failure" => evidence with { Scenarios = [evidence.Scenarios[0] with { SafeFailure = new string('x', 4097) }] },
            "skipped-reason" => evidence with
            {
                Failed = 0,
                Skipped = 1,
                Scenarios = [evidence.Scenarios[0] with { Outcome = FeatureScenarioOutcome.Skipped, SafeFailure = new string('x', 4097) }]
            },
            "unknown-outcome" => evidence with { Scenarios = [evidence.Scenarios[0] with { Outcome = (FeatureScenarioOutcome)99 }] },
            "null-scenarios" => evidence with { Scenarios = null! },
            "null-scenario" => evidence with { Scenarios = [null!] },
            "artifact-count" => evidence with
            {
                Artifacts = Enumerable.Range(0, 33)
                    .Select(index => new FeatureVerificationArtifact($"artifact-{index}", "application/json", 1, $"sha256:{new string('c', 64)}"))
                    .ToArray()
            },
            "artifact-name" => evidence with { Artifacts = [evidence.Artifacts[0] with { Name = new string('x', 257) }] },
            "duplicate-artifact" => evidence with { Artifacts = [evidence.Artifacts[0], evidence.Artifacts[0]] },
            "artifact-media-type" => evidence with { Artifacts = [evidence.Artifacts[0] with { MediaType = new string('x', 129) }] },
            "artifact-size" => evidence with { Artifacts = [evidence.Artifacts[0] with { SizeBytes = 1_048_577 }] },
            "artifact-digest" => evidence with { Artifacts = [evidence.Artifacts[0] with { Digest = new string('c', 64) }] },
            "null-artifacts" => evidence with { Artifacts = null! },
            "null-artifact" => evidence with { Artifacts = [null!] },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidity))
        };
        return Verification(evidence);
    }
}
