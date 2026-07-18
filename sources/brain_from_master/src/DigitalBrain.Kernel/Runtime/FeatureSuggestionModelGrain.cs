using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;
using Microsoft.Extensions.AI;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

public sealed class FeatureSuggestionModelGrain(IGrainFactory grainFactory, IChatClient chatClient) : Grain, IFeatureSuggestionModelGrain
{
    private static readonly JsonSerializerOptions StructuredJson = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<FeatureDraftPatch> SuggestAsync(SuggestFeatureChange command, CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        var ownerId = FeatureGrainIds.ParseHub(this.GetPrimaryKeyString());
        var hub = grainFactory.GetGrain<IFeatureHubGrain>(FeatureGrainIds.Hub(ownerId));
        var draft = await hub.ReadDraftAsync(command.DraftId).WaitAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        if (!string.Equals(draft.Status, "draft", StringComparison.Ordinal))
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);
        if (draft.Revision != command.ExpectedRevision)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Conflict);
        if (ConstrainedFeaturePackTemplates.TryMatchEnrichSalesforce(draft.Goal))
        {
            var constrained = FeatureDraftAuthoringTransitions.ValidatePatch(new FeatureDraftPatch(
                "patch-pending",
                draft.DraftId,
                draft.Revision,
                "Constrained Gmail + Web Search + Salesforce enrichment pack.",
                ConstrainedFeaturePackTemplates.SeedBehavior(draft.Goal),
                ConstrainedFeaturePackTemplates.SeedSource(draft.Goal)));
            constrained = constrained with { PatchId = PatchId(ownerId, draft, command.SuggestionId, constrained) };
            var currentConstrained = await hub.ReadDraftAsync(command.DraftId).WaitAsync(cancellationToken)
                ?? throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
            if (!string.Equals(currentConstrained.Status, "draft", StringComparison.Ordinal) ||
                currentConstrained.Revision != draft.Revision)
                throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Conflict);
            return constrained;
        }
        var prompt = BuildPrompt(draft, command.Guidance);
        if (Encoding.UTF8.GetByteCount(prompt) > FeatureLimits.DraftSuggestionPayloadUtf8Bytes)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Limit);
        ChatResponse<FeatureSuggestionContent> response;
        try
        {
            response = await chatClient.GetResponseAsync<FeatureSuggestionContent>(
                new ChatMessage(ChatRole.User, prompt),
                StructuredJson,
                useJsonSchemaResponseFormat: true,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureCommandRejectedException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        }
        if (Encoding.UTF8.GetByteCount(response.Text) > FeatureLimits.DraftSuggestionPayloadUtf8Bytes)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        if (!response.TryGetResult(out var content) || content is null)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        FeatureDraftPatch patch;
        try
        {
            patch = FeatureDraftAuthoringTransitions.ValidatePatch(new FeatureDraftPatch(
                "patch-pending",
                draft.DraftId,
                draft.Revision,
                content.Summary,
                content.ReplacementBehavior,
                content.ReplacementSource));
        }
        catch (ArgumentException)
        {
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        }
        patch = patch with { PatchId = PatchId(ownerId, draft, command.SuggestionId, patch) };
        var current = await hub.ReadDraftAsync(command.DraftId).WaitAsync(cancellationToken)
            ?? throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Unavailable);
        if (!string.Equals(current.Status, "draft", StringComparison.Ordinal) || current.Revision != draft.Revision)
            throw new FeatureCommandRejectedException(FeatureCommandRejectionReason.Conflict);
        return patch;
    }

    private static void ValidateCommand(SuggestFeatureChange command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.DraftId);
        DemandText(command.DraftId.Value, 128, nameof(command.DraftId));
        if (command.ExpectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(command.ExpectedRevision));
        DemandText(command.Guidance, FeatureLimits.DraftSuggestionGuidanceCharacters, nameof(command.Guidance));
        DemandText(command.SuggestionId, FeatureLimits.DraftPatchIdCharacters, nameof(command.SuggestionId));
    }

    private static string BuildPrompt(FeatureDraft draft, string guidance) => $$"""
        Produce one reviewable Suggested Change for the current Feature Draft.
        Return a bounded safe summary plus a complete replacement Behavior and complete replacement Source Snapshot.
        Do not include owner, Draft identity, revision, patch identity, idempotency, approval, grant, installation, or release fields.
        Current goal: {{draft.Goal}}
        Current Behavior: {{JsonSerializer.Serialize(draft.Behavior, StructuredJson)}}
        Current Source Snapshot: {{JsonSerializer.Serialize(draft.Source, StructuredJson)}}
        User guidance: {{guidance}}
        """;

    private static string PatchId(
        BrainOwnerId ownerId,
        FeatureDraft draft,
        string suggestionId,
        FeatureDraftPatch patch)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(
            new FeaturePatchIdentity(
                ownerId.Value,
                draft.DraftId.Value,
                draft.Revision,
                suggestionId,
                patch.Summary,
                patch.ReplacementBehavior,
                patch.ReplacementSource),
            StructuredJson);
        return "patch-" + Convert.ToHexStringLower(SHA256.HashData(canonical))[..32];
    }

    private static void DemandText(string value, int maximumCharacters, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumCharacters || value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical value is required.", parameterName);
    }
}

internal sealed record FeaturePatchIdentity(
    string OwnerId,
    string DraftId,
    long Revision,
    string SuggestionId,
    string Summary,
    FeatureBehavior ReplacementBehavior,
    FeatureSourceSnapshot ReplacementSource);

internal sealed record FeatureSuggestionContent(
    string Summary,
    FeatureBehavior ReplacementBehavior,
    FeatureSourceSnapshot ReplacementSource);
