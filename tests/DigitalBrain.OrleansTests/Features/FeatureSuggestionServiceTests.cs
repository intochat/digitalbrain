extern alias McpProject;

using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using FeatureSuggestionService = McpProject::DigitalBrain.Mcp.FeatureSuggestionService;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.OrleansTests.Features;

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class FeatureSuggestionServiceTests(FeatureGrainClusterFixture fixture)
{
    [Fact]
    public async Task The_service_returns_a_review_only_patch_from_the_owner_local_Draft()
    {
        var context = Context("owner-suggestion-service");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-suggestion-service",
            "Create a reviewed Feature",
            fixture.Time.GetUtcNow(),
            "conversation-suggestion-service"));
        fixture.SuggestionModel.RespondWith(Response());
        var service = new FeatureSuggestionService(fixture.Cluster.Client);

        var patch = await service.SuggestAsync(
            context,
            new SuggestFeatureChange(draft.DraftId, draft.Revision, "Add the safe outcome", "suggestion-service"));

        Assert.Equal(draft.DraftId, patch.DraftId);
        Assert.Equal(draft.Revision, patch.BaseRevision);
        var unchanged = Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId));
        Assert.Equal(draft.Revision, unchanged.Revision);
        Assert.Equal(draft.Behavior.Scenarios, unchanged.Behavior.Scenarios);
        Assert.Equal(draft.Source.Files, unchanged.Source.Files);
        Assert.Null(unchanged.Verification);
        Assert.Equal(1, fixture.SuggestionModel.CallCount);
    }

    [Fact]
    public async Task The_service_rejects_cross_Owner_stale_and_unauthorized_requests_before_model_use()
    {
        var context = Context("owner-suggestion-service-authority");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(context.OwnerId));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-suggestion-service-authority",
            "Create an owner-bound Feature",
            fixture.Time.GetUtcNow(),
            "conversation-suggestion-service-authority"));
        fixture.SuggestionModel.RespondWith(Response());
        var service = new FeatureSuggestionService(fixture.Cluster.Client);
        var command = new SuggestFeatureChange(draft.DraftId, draft.Revision, "Stay owner-local", "suggestion-authority");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SuggestAsync(Context("owner-suggestion-service-other"), command));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SuggestAsync(Context(context.OwnerId.Value, []), command));
        await hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
            draft.DraftId,
            new FeatureBehavior([new FeatureScenario("scenario-stale", "Stale", "a Draft exists", "it changes", "the revision advances")]),
            draft.Revision,
            "suggestion-service-stale",
            fixture.Time.GetUtcNow().AddMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SuggestAsync(context, command));

        Assert.Equal(0, fixture.SuggestionModel.CallCount);
    }

    [Fact]
    public async Task Feature_authority_requires_a_defined_authenticated_assurance_and_an_ordinal_grant()
    {
        var context = Context("owner-suggestion-service-auth-hardening");
        var service = new FeatureSuggestionService(fixture.Cluster.Client);
        var command = new SuggestFeatureChange(new FeatureDraftId("draft-auth-hardening"), 0, "Require exact authority", "suggestion-auth-hardening");
        RuntimeRequestContext[] rejected =
        [
            context with { Assurance = AuthAssurance.None },
            context with { Assurance = (AuthAssurance)999 },
            context with { Grants = new HashSet<string>(["FEATURE.MANAGE"], StringComparer.OrdinalIgnoreCase) }
        ];

        foreach (var candidate in rejected)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SuggestAsync(candidate, command));
    }

    private static RuntimeRequestContext Context(string owner, string[]? grants = null) => new(
        new BrainOwnerId(owner),
        new ActorId("actor-feature-author"),
        new SessionId("session-feature-author"),
        AuthAssurance.Oidc,
        "correlation-feature-author",
        null,
        new HashSet<string>(grants ?? ["feature.manage"], StringComparer.Ordinal),
        "conversation-feature-author");

    private static string Response() => """
        {
          "summary": "Replace the Draft safely",
          "replacementBehavior": {
            "scenarios": [
              {
                "scenarioId": "scenario-service",
                "name": "Create an outcome",
                "given": "the user has a request",
                "when": "the Feature runs",
                "then": "the outcome is available"
              }
            ]
          },
          "replacementSource": {
            "implementationProjectPath": "src/Feature/Feature.csproj",
            "scenarioProjectPath": "tests/Feature.Scenarios/Feature.Scenarios.csproj",
            "files": [
              { "path": "src/Feature/Feature.csproj", "content": "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>" },
              { "path": "src/Feature/Feature.cs", "content": "namespace RuntimeAuthored; public sealed class Feature;" },
              { "path": "tests/Feature.Scenarios/Feature.Scenarios.csproj", "content": "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>" }
            ]
          }
        }
        """;
}
