using System.Reflection;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Serialization;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureDraftTransitionTests
{
    private const string OwnerScope = "owner-scope-1";
    private const string ConversationId = "conversation-1";
    private const string Goal = "Research Acme and create a text file";
    private static readonly FeatureHubState EmptyState = FeatureHubState.Empty;
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FeatureDraft_replaces_the_legacy_proposal_CLR_type()
    {
        var contracts = typeof(CreateFeatureDraft).Assembly;

        Assert.NotNull(contracts.GetType("DigitalBrain.Kernel.Contracts.FeatureDraft"));
        Assert.Null(contracts.GetType("DigitalBrain.Kernel.Contracts.FeatureDraftProposal"));
        Assert.Equal(
            "digitalbrain.feature.draft-proposal.v1",
            typeof(FeatureDraft).GetCustomAttribute<AliasAttribute>()?.Alias);
        Assert.Equal(
            "digitalbrain.v3.feature-hub-state",
            typeof(FeatureHubState).GetCustomAttribute<AliasAttribute>()?.Alias);
        Assert.Equal(
            "digitalbrain.feature.create-draft.v1",
            typeof(CreateFeatureDraft).GetCustomAttribute<AliasAttribute>()?.Alias);
    }

    [Fact]
    public void Legacy_stored_draft_shape_is_readable_as_a_FeatureDraft()
    {
        var legacyServices = new ServiceCollection();
        legacyServices.AddSerializer(builder => builder.AddAssembly(typeof(LegacyFeatureHubState).Assembly));
        using var legacyProvider = legacyServices.BuildServiceProvider();
        var legacy = new LegacyFeatureDraftProposal("proposal-legacy", "operation-legacy", Goal, "draft", Now);
        var bytes = legacyProvider.GetRequiredService<Serializer<LegacyFeatureHubState>>()
            .SerializeToArray(new LegacyFeatureHubState([legacy]));

        var currentServices = new ServiceCollection();
        currentServices.AddSerializer(builder => builder
            .AddAssembly(typeof(FeatureDraft).Assembly)
            .AddAssembly(typeof(FeatureHubState).Assembly));
        using var currentProvider = currentServices.BuildServiceProvider();

        var state = currentProvider.GetRequiredService<Serializer<FeatureHubState>>().Deserialize(bytes);
        var draft = Assert.Single(state.Drafts ?? []);

        Assert.Equal(new FeatureDraftId("proposal-legacy"), draft.DraftId);
        Assert.Equal("operation-legacy", draft.OriginatingRequest.OperationId);
        Assert.Equal(Goal, draft.OriginatingRequest.Text);
        Assert.Equal(Goal, draft.Goal);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(Now, draft.CreatedAt);
        Assert.Equal(FeatureDraft.LegacyMissingConversationId, draft.OriginatingRequest.ConversationId);

        var nullBytes = legacyProvider.GetRequiredService<Serializer<LegacyFeatureHubState>>()
            .SerializeToArray(new LegacyFeatureHubState(null));
        var nullState = currentProvider.GetRequiredService<Serializer<FeatureHubState>>().Deserialize(nullBytes);
        Assert.Null(nullState.Drafts);
    }

    [Fact]
    public void Legacy_create_command_without_ConversationId_remains_accepted()
    {
        var legacyServices = new ServiceCollection();
        legacyServices.AddSerializer(builder => builder.AddAssembly(typeof(LegacyCreateFeatureDraft).Assembly));
        using var legacyProvider = legacyServices.BuildServiceProvider();
        var bytes = legacyProvider.GetRequiredService<Serializer<LegacyCreateFeatureDraft>>()
            .SerializeToArray(new LegacyCreateFeatureDraft("operation-legacy", Goal, Now));
        var currentServices = new ServiceCollection();
        currentServices.AddSerializer(builder => builder.AddAssembly(typeof(CreateFeatureDraft).Assembly));
        using var currentProvider = currentServices.BuildServiceProvider();
        var command = currentProvider.GetRequiredService<Serializer<CreateFeatureDraft>>().Deserialize(bytes);

        var created = FeatureHubTransitions.CreateDraft(EmptyState, OwnerScope, command);

        Assert.Equal(FeatureDraft.LegacyMissingConversationId, created.Draft.OriginatingRequest.ConversationId);
    }

    [Fact]
    public void Explicit_legacy_text_is_not_the_missing_field_marker_and_the_marker_is_reserved()
    {
        var first = FeatureHubTransitions.CreateDraft(
            EmptyState,
            OwnerScope,
            new CreateFeatureDraft("operation-legacy-text", Goal, Now, "legacy"));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.CreateDraft(
            first.State,
            OwnerScope,
            new CreateFeatureDraft("operation-legacy-text", Goal, Now, "another-conversation")));
        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateDraft(
            EmptyState,
            OwnerScope,
            new CreateFeatureDraft("operation-reserved", Goal, Now, FeatureDraft.LegacyMissingConversationId)));
    }

    [Fact]
    public void CreateDraft_is_idempotent_for_the_same_operation()
    {
        var request = new CreateFeatureDraft("operation-1", Goal, Now, ConversationId);

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
            new CreateFeatureDraft("operation-1", new string('x', 4097), Now, ConversationId)));
        Assert.Throws<ArgumentException>(() => FeatureHubTransitions.CreateDraft(
            EmptyState,
            OwnerScope,
            new CreateFeatureDraft("operation-2", "unsafe\0prompt", Now, ConversationId)));
    }

    [Fact]
    public void CreateDraft_rejects_a_different_goal_for_the_same_operation()
    {
        var first = FeatureHubTransitions.CreateDraft(EmptyState, OwnerScope, new CreateFeatureDraft("operation-1", Goal, Now, ConversationId));

        Assert.Throws<FeatureConcurrencyException>(() => FeatureHubTransitions.CreateDraft(
            first.State,
            OwnerScope,
            new CreateFeatureDraft("operation-1", "Research a different company instead", Now, ConversationId)));
    }

    [Fact]
    public void CreateDraft_accepts_one_hundred_drafts_and_rejects_the_next()
    {
        var state = EmptyState;
        for (var index = 0; index < FeatureLimits.DraftsPerOwner; index++)
        {
            state = FeatureHubTransitions.CreateDraft(state, OwnerScope, new CreateFeatureDraft($"operation-{index}", Goal, Now, ConversationId)).State;
        }

        Assert.Equal(FeatureLimits.DraftsPerOwner, state.Drafts?.Length);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureHubTransitions.CreateDraft(
            state,
            OwnerScope,
            new CreateFeatureDraft("operation-overflow", Goal, Now, ConversationId)));
    }

    [Fact]
    public void CreateDraft_derives_a_stable_draft_id_from_the_owner_and_operation()
    {
        var first = FeatureHubTransitions.CreateDraft(EmptyState, OwnerScope, new CreateFeatureDraft("operation-1", Goal, Now, ConversationId));
        var second = FeatureHubTransitions.CreateDraft(first.State, OwnerScope, new CreateFeatureDraft("operation-1", Goal, Now, ConversationId));

        Assert.Matches("^proposal-[0-9a-f]{32}$", first.Draft.DraftId.Value);
        Assert.Equal(first.Draft.DraftId, second.Draft.DraftId);
    }

    [Fact]
    public void CreateDraft_seeds_the_originating_request_Behavior_and_Source_Snapshot()
    {
        var draft = FeatureHubTransitions.CreateDraft(
            EmptyState,
            OwnerScope,
            new CreateFeatureDraft("operation-1", Goal, Now, ConversationId)).Draft;

        Assert.Equal(new OriginatingRequest("operation-1", ConversationId, Goal), draft.OriginatingRequest);
        Assert.Single(draft.Behavior.Scenarios);
        Assert.All(draft.Behavior.Scenarios, scenario =>
        {
            Assert.NotEmpty(scenario.ScenarioId);
            Assert.NotEmpty(scenario.Name);
            Assert.NotEmpty(scenario.Given);
            Assert.NotEmpty(scenario.When);
            Assert.NotEmpty(scenario.Then);
        });
        Assert.Contains(draft.Source.Files, file => file.Path == draft.Source.ImplementationProjectPath);
        Assert.Contains(draft.Source.Files, file => file.Path == draft.Source.ScenarioProjectPath);
        Assert.Null(draft.Verification);
        Assert.Null(draft.InstallationId);
        Assert.Equal(0, draft.Revision);
        Assert.Equal(Now, draft.UpdatedAt);
    }

    [GenerateSerializer, Alias("digitalbrain.feature.draft-proposal.x1")]
    internal sealed record LegacyFeatureDraftProposal(
        [property: Id(0)] string ProposalId,
        [property: Id(1)] string OperationId,
        [property: Id(2)] string Goal,
        [property: Id(3)] string Status,
        [property: Id(4)] DateTimeOffset CreatedAt);

    [GenerateSerializer, Alias("digitalbrain.v3.feature-hub-statz")]
    internal sealed record LegacyFeatureHubState(
        [property: Id(7)] LegacyFeatureDraftProposal[]? Drafts);

    [GenerateSerializer, Alias("digitalbrain.feature.create-draft.x1")]
    internal sealed record LegacyCreateFeatureDraft(
        [property: Id(0)] string OperationId,
        [property: Id(1)] string Goal,
        [property: Id(2)] DateTimeOffset RequestedAt);
}
