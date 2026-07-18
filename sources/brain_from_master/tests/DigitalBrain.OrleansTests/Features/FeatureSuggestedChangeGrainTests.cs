using DigitalBrain.Kernel.Contracts;
using Orleans;

namespace DigitalBrain.OrleansTests.Features;

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class FeatureSuggestedChangeGrainTests(FeatureGrainClusterFixture fixture)
{
    [Fact]
    public async Task A_Suggested_Change_is_accepted_only_inside_its_Owner_Scope_and_replays_after_reactivation()
    {
        var owner = new BrainOwnerId("owner-suggested-change-grain");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var otherHub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(new BrainOwnerId("owner-suggested-change-other")));
        var createdAt = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-suggested-change-grain",
            "Create an owner-local Feature",
            createdAt,
            "conversation-suggested-change-grain"));
        var patch = Patch(draft);
        var command = new AcceptSuggestedChange(patch, draft.Revision, "accept-grain", createdAt.AddMinutes(1));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => otherHub.AcceptSuggestedChangeAsync(command));
        var accepted = await hub.AcceptSuggestedChangeAsync(command);

        Assert.Equal(1, accepted.Revision);
        Assert.Equal(patch.ReplacementBehavior.Scenarios, accepted.Behavior.Scenarios);
        Assert.Equal(patch.ReplacementSource.ImplementationProjectPath, accepted.Source.ImplementationProjectPath);
        Assert.Equal(patch.ReplacementSource.ScenarioProjectPath, accepted.Source.ScenarioProjectPath);
        Assert.Equal(patch.ReplacementSource.Files, accepted.Source.Files);
        await fixture.Cluster.DeactivateAsync((IAddressable)hub);
        AssertSameDraft(accepted, await hub.AcceptSuggestedChangeAsync(command));
        Assert.Equal(1, (await hub.ReadDraftAsync(draft.DraftId))?.Revision);
    }

    [Fact]
    public async Task Rejecting_a_Suggested_Change_through_the_grain_performs_no_write()
    {
        var owner = new BrainOwnerId("owner-reject-suggested-change-grain");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var createdAt = fixture.Time.GetUtcNow();
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-reject-suggested-change-grain",
            "Create an owner-local Feature",
            createdAt,
            "conversation-reject-suggested-change-grain"));
        var patch = Patch(draft);

        var rejected = await hub.RejectSuggestedChangeAsync(new RejectSuggestedChange(
            draft.DraftId,
            patch.PatchId,
            patch.BaseRevision,
            draft.Revision));

        AssertSameDraft(draft, rejected);
        await fixture.Cluster.DeactivateAsync((IAddressable)hub);
        AssertSameDraft(draft, Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId)));
    }

    private static FeatureDraftPatch Patch(FeatureDraft draft) => new(
        "patch-grain",
        draft.DraftId,
        draft.Revision,
        "Replace the owner-local Draft",
        new FeatureBehavior([
            new FeatureScenario(
                "scenario-grain-patch",
                "Create an outcome",
                "the owner has a request",
                "the Feature runs",
                "the outcome is available")
        ]),
        new FeatureSourceSnapshot(
            "src/Feature/Feature.csproj",
            "tests/Feature.Scenarios/Feature.Scenarios.csproj",
            [
                new FeatureSourceFile("src/Feature/Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                new FeatureSourceFile("src/Feature/Feature.cs", "namespace RuntimeAuthored; public sealed class Feature;"),
                new FeatureSourceFile("tests/Feature.Scenarios/Feature.Scenarios.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
            ]));

    private static void AssertSameDraft(FeatureDraft expected, FeatureDraft actual)
    {
        Assert.Equal(expected.DraftId, actual.DraftId);
        Assert.Equal(expected.OriginatingRequest, actual.OriginatingRequest);
        Assert.Equal(expected.Goal, actual.Goal);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Behavior.Scenarios, actual.Behavior.Scenarios);
        Assert.Equal(expected.Source.ImplementationProjectPath, actual.Source.ImplementationProjectPath);
        Assert.Equal(expected.Source.ScenarioProjectPath, actual.Source.ScenarioProjectPath);
        Assert.Equal(expected.Source.Files, actual.Source.Files);
        Assert.Equal(expected.Verification, actual.Verification);
        Assert.Equal(expected.InstallationId, actual.InstallationId);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
    }
}
