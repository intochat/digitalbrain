using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureDraftTransitionTests
{
    private const string OwnerScope = "owner-scope-1";
    private const string Goal = "Research Acme and create a text file";
    private static readonly FeatureHubState EmptyState = FeatureHubState.Empty;
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDraft_is_idempotent_for_the_same_operation()
    {
        var request = new CreateFeatureDraft("operation-1", Goal, Now);

        var first = FeatureHubTransitions.CreateDraft(EmptyState, OwnerScope, request);
        var second = FeatureHubTransitions.CreateDraft(first.State, OwnerScope, request);

        Assert.Equal(first.Draft, second.Draft);
        Assert.Same(first.State, second.State);
    }

    [Fact]
    public void CreateDraft_rejects_unbounded_or_control_character_prompts()
    {
        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateDraft(
            EmptyState,
            OwnerScope,
            new CreateFeatureDraft("operation-1", new string('x', 4097), Now)));
        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateDraft(
            EmptyState,
            OwnerScope,
            new CreateFeatureDraft("operation-2", "unsafe\0prompt", Now)));
    }

    [Fact]
    public void CreateDraft_rejects_a_different_goal_for_the_same_operation()
    {
        var first = FeatureHubTransitions.CreateDraft(EmptyState, OwnerScope, new CreateFeatureDraft("operation-1", Goal, Now));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.CreateDraft(
            first.State,
            OwnerScope,
            new CreateFeatureDraft("operation-1", "Research a different company instead", Now)));
    }

    [Fact]
    public void CreateDraft_accepts_one_hundred_drafts_and_rejects_the_next()
    {
        var state = EmptyState;
        for (var index = 0; index < FeatureLimits.DraftsPerOwner; index++)
        {
            state = FeatureHubTransitions.CreateDraft(state, OwnerScope, new CreateFeatureDraft($"operation-{index}", Goal, Now)).State;
        }

        Assert.Equal(FeatureLimits.DraftsPerOwner, state.Drafts?.Length);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureHubTransitions.CreateDraft(
            state,
            OwnerScope,
            new CreateFeatureDraft("operation-overflow", Goal, Now)));
    }

    [Fact]
    public void CreateDraft_derives_a_stable_proposal_id_from_the_owner_and_operation()
    {
        var first = FeatureHubTransitions.CreateDraft(EmptyState, OwnerScope, new CreateFeatureDraft("operation-1", Goal, Now));
        var second = FeatureHubTransitions.CreateDraft(first.State, OwnerScope, new CreateFeatureDraft("operation-1", Goal, Now));

        Assert.Matches("^proposal-[0-9a-f]{32}$", first.Draft.ProposalId);
        Assert.Equal(first.Draft.ProposalId, second.Draft.ProposalId);
    }
}
