using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.AI;

namespace DigitalBrain.OrleansTests.Features;

[Collection(FeatureGrainClusterCollection.Name)]
public sealed class FeatureSuggestionModelGrainTests(FeatureGrainClusterFixture fixture)
{
    [Fact]
    public async Task Structured_Suggested_Changes_are_bound_to_the_server_read_Owner_Draft_and_revision()
    {
        var owner = new BrainOwnerId("owner-structured-suggestion");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-structured-suggestion",
            "Create a structured Feature",
            fixture.Time.GetUtcNow(),
            "conversation-structured-suggestion"));
        fixture.SuggestionModel.RespondWith(Response("safe"));
        var model = fixture.Grain<IFeatureSuggestionModelGrain>(FeatureGrainIds.Hub(owner));

        var patch = await model.SuggestAsync(new SuggestFeatureChange(
            draft.DraftId,
            draft.Revision,
            "Add the reviewed outcome",
            "suggestion-safe"));

        Assert.StartsWith("patch-", patch.PatchId, StringComparison.Ordinal);
        Assert.Equal(draft.DraftId, patch.DraftId);
        Assert.Equal(draft.Revision, patch.BaseRevision);
        Assert.Equal("Replace the Draft safely", patch.Summary);
        Assert.Equal(1, fixture.SuggestionModel.CallCount);
        Assert.IsType<ChatResponseFormatJson>(fixture.SuggestionModel.LastOptions?.ResponseFormat);
        Assert.Contains("Add the reviewed outcome", fixture.SuggestionModel.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_identity_binds_the_full_validated_model_content()
    {
        var owner = new BrainOwnerId("owner-suggestion-content-identity");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-suggestion-content-identity",
            "Bind Suggested Change identity to content",
            fixture.Time.GetUtcNow(),
            "conversation-suggestion-content-identity"));
        var model = fixture.Grain<IFeatureSuggestionModelGrain>(FeatureGrainIds.Hub(owner));
        var command = new SuggestFeatureChange(
            draft.DraftId,
            draft.Revision,
            "Produce a complete Suggested Change",
            "suggestion-content-identity");
        fixture.SuggestionModel.RespondWith(Response("first"));
        var first = await model.SuggestAsync(command);
        fixture.SuggestionModel.RespondWith(Response("second"));

        var second = await model.SuggestAsync(command);

        Assert.NotEqual(first.PatchId, second.PatchId);
        Assert.Equal(first.DraftId, second.DraftId);
        Assert.Equal(first.BaseRevision, second.BaseRevision);
    }

    [Fact]
    public async Task The_RuntimeHost_suggestion_seam_independently_rejects_cross_Owner_and_stale_Draft_lookups()
    {
        var owner = new BrainOwnerId("owner-suggestion-authority");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-suggestion-authority",
            "Create an authoritative Feature",
            fixture.Time.GetUtcNow(),
            "conversation-suggestion-authority"));
        fixture.SuggestionModel.RespondWith(Response("authority"));
        var command = new SuggestFeatureChange(draft.DraftId, draft.Revision, "Keep authority server-side", "suggestion-authority");
        var other = fixture.Grain<IFeatureSuggestionModelGrain>(FeatureGrainIds.Hub(new BrainOwnerId("owner-suggestion-other")));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => other.SuggestAsync(command));
        Assert.Equal(0, fixture.SuggestionModel.CallCount);

        await hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
            draft.DraftId,
            new FeatureBehavior([
                new FeatureScenario("scenario-newer", "Newer", "the Draft exists", "Behavior changes", "the revision advances")
            ]),
            draft.Revision,
            "behavior-newer-suggestion",
            fixture.Time.GetUtcNow().AddMinutes(1)));
        var model = fixture.Grain<IFeatureSuggestionModelGrain>(FeatureGrainIds.Hub(owner));

        var stale = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => model.SuggestAsync(command));
        Assert.Equal(FeatureCommandRejectionReason.Conflict, stale.Reason);
        Assert.Equal(0, fixture.SuggestionModel.CallCount);
    }

    [Fact]
    public async Task A_Draft_edit_during_model_generation_prevents_a_stale_patch_from_being_returned()
    {
        var owner = new BrainOwnerId("owner-suggestion-model-race");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-suggestion-model-race",
            "Fence a long-running suggestion",
            fixture.Time.GetUtcNow(),
            "conversation-suggestion-model-race"));
        fixture.SuggestionModel.RespondWith(Response("race"), async () =>
        {
            await hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
                draft.DraftId,
                new FeatureBehavior([
                    new FeatureScenario("scenario-raced", "Raced", "a model call is active", "the Draft changes", "the stale patch is rejected")
                ]),
                draft.Revision,
                "suggestion-model-race-edit",
                fixture.Time.GetUtcNow().AddMinutes(1)));
        });
        var model = fixture.Grain<IFeatureSuggestionModelGrain>(FeatureGrainIds.Hub(owner));

        var stale = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => model.SuggestAsync(new SuggestFeatureChange(
            draft.DraftId,
            draft.Revision,
            "Do not return a stale patch",
            "suggestion-model-race")));

        Assert.Equal(FeatureCommandRejectionReason.Conflict, stale.Reason);
        Assert.Equal(1, fixture.SuggestionModel.CallCount);
        Assert.Equal(draft.Revision + 1, (await hub.ReadDraftAsync(draft.DraftId))?.Revision);
    }

    [Fact]
    public async Task Model_output_cannot_supply_server_owned_Suggested_Change_coordinates()
    {
        var owner = new BrainOwnerId("owner-untrusted-suggestion-coordinate");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-untrusted-suggestion-coordinate",
            "Create a coordinate-safe Feature",
            fixture.Time.GetUtcNow(),
            "conversation-untrusted-suggestion-coordinate"));
        fixture.SuggestionModel.RespondWith(Response("untrusted").TrimEnd('}') + ",\"patchId\":\"model-selected\"}");
        var model = fixture.Grain<IFeatureSuggestionModelGrain>(FeatureGrainIds.Hub(owner));

        var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => model.SuggestAsync(new SuggestFeatureChange(
            draft.DraftId,
            draft.Revision,
            "Do not trust model coordinates",
            "suggestion-untrusted-coordinate")));

        Assert.Equal(FeatureCommandRejectionReason.Unavailable, rejected.Reason);
    }

    [Fact]
    public async Task Null_malformed_oversized_and_noncanonical_model_outputs_are_never_returned_as_patches()
    {
        var owner = new BrainOwnerId("owner-invalid-suggestion-output");
        var hub = fixture.Grain<IFeatureHubGrain>(FeatureGrainIds.Hub(owner));
        var draft = await hub.CreateDraftAsync(new CreateFeatureDraft(
            "operation-invalid-suggestion-output",
            "Reject unsafe model output",
            fixture.Time.GetUtcNow(),
            "conversation-invalid-suggestion-output"));
        var model = fixture.Grain<IFeatureSuggestionModelGrain>(FeatureGrainIds.Hub(owner));
        var valid = Response("safe");
        string[] invalidResponses =
        [
            "null",
            "{\"summary\":",
            valid.Replace("Replace the Draft safely", " Replace the Draft safely", StringComparison.Ordinal),
            valid.Replace("src/Feature/Feature.cs", "../Feature.cs", StringComparison.Ordinal),
            valid.Replace("Replace the Draft safely", new string('x', 5 * 1024 * 1024 + 1), StringComparison.Ordinal),
            valid.Replace("\"replacementBehavior\": {", "\"replacementBehavior\": null, \"ignored\": {", StringComparison.Ordinal)
        ];

        foreach (var response in invalidResponses)
        {
            fixture.SuggestionModel.RespondWith(response);
            var rejected = await Assert.ThrowsAsync<FeatureCommandRejectedException>(() => model.SuggestAsync(new SuggestFeatureChange(
                draft.DraftId,
                draft.Revision,
                "Return only a safe complete patch",
                "suggestion-invalid-output")));
            Assert.Equal(FeatureCommandRejectionReason.Unavailable, rejected.Reason);
        }

        var unchanged = Assert.IsType<FeatureDraft>(await hub.ReadDraftAsync(draft.DraftId));
        Assert.Equal(draft.Revision, unchanged.Revision);
        Assert.Null(unchanged.Verification);
    }

    private static string Response(string suffix) => $$"""
        {
          "summary": "Replace the Draft {{suffix}}ly",
          "replacementBehavior": {
            "scenarios": [
              {
                "scenarioId": "scenario-{{suffix}}",
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
