using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureSuggestedChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accepting_a_Suggested_Change_replaces_Behavior_and_Source_atomically()
    {
        var created = Create();
        var patch = Patch(created.Draft, "accepted");

        var accepted = FeatureDraftAuthoringTransitions.AcceptSuggestedChange(
            created.State,
            new AcceptSuggestedChange(patch, 0, "accept-1", Now.AddMinutes(1)));

        Assert.Equal(patch.ReplacementBehavior, accepted.Draft.Behavior);
        Assert.Equal(patch.ReplacementSource, accepted.Draft.Source);
        Assert.Equal(1, accepted.Draft.Revision);
        Assert.Null(accepted.Draft.Verification);
    }

    [Fact]
    public void A_Suggested_Change_cannot_target_an_old_Draft_Revision()
    {
        var created = Create();
        var patch = Patch(created.Draft, "stale");
        var revised = FeatureDraftAuthoringTransitions.ReviseBehavior(
            created.State,
            new ReviseFeatureBehavior(created.Draft.DraftId, Behavior("newer"), 0, "behavior-newer", Now.AddMinutes(1)));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureDraftAuthoringTransitions.AcceptSuggestedChange(
            revised.State,
            new AcceptSuggestedChange(patch, 0, "accept-stale", Now.AddMinutes(2))));
    }

    [Fact]
    public void Accepting_the_same_Suggested_Change_replays_and_an_altered_payload_conflicts()
    {
        var created = Create();
        var patch = Patch(created.Draft, "replay");
        var command = new AcceptSuggestedChange(patch, 0, "accept-replay", Now.AddMinutes(1));
        var first = FeatureDraftAuthoringTransitions.AcceptSuggestedChange(created.State, command);
        var later = FeatureDraftAuthoringTransitions.ReviseSource(
            first.State,
            new ReviseFeatureSource(created.Draft.DraftId, Source("later"), 1, "source-later", Now.AddMinutes(2)));

        var replay = FeatureDraftAuthoringTransitions.AcceptSuggestedChange(later.State, command);

        Assert.Equal(first.Draft, replay.Draft);
        Assert.Same(later.State, replay.State);
        var altered = command with { Patch = patch with { Summary = "Altered review summary" } };
        Assert.Throws<FeatureConcurrencyException>(() =>
            FeatureDraftAuthoringTransitions.AcceptSuggestedChange(later.State, altered));
    }

    [Fact]
    public void Rejecting_a_Suggested_Change_leaves_the_Draft_and_replay_ledger_unchanged()
    {
        var created = Create();
        var patch = Patch(created.Draft, "rejected");
        var beforeReplays = created.State.DraftReplays;

        var rejected = FeatureDraftAuthoringTransitions.RejectSuggestedChange(
            created.State,
            new RejectSuggestedChange(
                created.Draft.DraftId,
                patch.PatchId,
                patch.BaseRevision,
                0));

        Assert.Same(created.State, rejected.State);
        Assert.Same(created.Draft, rejected.Draft);
        Assert.Same(beforeReplays, rejected.State.DraftReplays);
    }

    [Fact]
    public void Accepting_a_Suggested_Change_invalidates_the_Verified_Candidate()
    {
        var created = Create();
        var verified = FeatureDraftAuthoringTransitions.RecordVerification(
            created.State,
            new RecordFeatureVerification(
                created.Draft.DraftId,
                FeatureVerificationTestData.Passing(
                    new ReleaseDigest(new string('a', 64)),
                    created.Draft.Source,
                    1,
                    Now.AddMinutes(1)),
                0,
                "verification-1"));
        var patch = Patch(verified.Draft, "after-verification");

        var accepted = FeatureDraftAuthoringTransitions.AcceptSuggestedChange(
            verified.State,
            new AcceptSuggestedChange(patch, 1, "accept-after-verification", Now.AddMinutes(2)));

        Assert.Null(accepted.Draft.Verification);
        Assert.Equal(2, accepted.Draft.Revision);
    }

    private static FeatureCreateDraftTransition Create() =>
        FeatureHubTransitions.CreateDraft(
            FeatureHubState.Empty,
            "owner-suggested-change",
            new CreateFeatureDraft("operation-suggested-change", "Create a useful Feature", Now, "conversation-suggested-change"));

    private static FeatureDraftPatch Patch(FeatureDraft draft, string suffix) => new(
        $"patch-{suffix}",
        draft.DraftId,
        draft.Revision,
        $"Replace the Draft for {suffix}",
        Behavior(suffix),
        Source(suffix));

    private static FeatureBehavior Behavior(string suffix) => new([
        new FeatureScenario(
            $"scenario-{suffix}",
            "Create an outcome",
            "the user has a request",
            "the Feature runs",
            "the outcome is available")
    ]);

    private static FeatureSourceSnapshot Source(string suffix) => new(
        "src/Feature/Feature.csproj",
        "tests/Feature.Scenarios/Feature.Scenarios.csproj",
        [
            new FeatureSourceFile("src/Feature/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile("src/Feature/Feature.cs", $"namespace RuntimeAuthored; public sealed class Feature{suffix};"),
            new FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            new FeatureSourceFile("tests/Feature.Scenarios/Feature.feature", $"Feature: {suffix}")
        ]);
}
