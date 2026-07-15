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
            throw new InvalidOperationException("An installed Feature Draft cannot receive Suggested Changes.");
        if (draft.Revision != command.ExpectedRevision)
            throw new InvalidOperationException("The Draft Revision changed.");
        var prompt = BuildPrompt(draft, command.Guidance);
        if (Encoding.UTF8.GetByteCount(prompt) > FeatureLimits.DraftSuggestionPayloadUtf8Bytes)
            throw new InvalidOperationException("The Feature suggestion prompt exceeds its bound.");
        var response = await chatClient.GetResponseAsync<FeatureSuggestionContent>(
            new ChatMessage(ChatRole.User, prompt),
            StructuredJson,
            useJsonSchemaResponseFormat: true,
            cancellationToken: cancellationToken);
        if (Encoding.UTF8.GetByteCount(response.Text) > FeatureLimits.DraftSuggestionPayloadUtf8Bytes)
            throw new InvalidOperationException("The Feature suggestion response exceeds its bound.");
        if (!response.TryGetResult(out var content) || content is null)
            throw new InvalidOperationException("The Feature suggestion model returned no structured patch.");
        var patch = FeatureDraftAuthoringTransitions.ValidatePatch(new FeatureDraftPatch(
            PatchId(ownerId, draft, command.SuggestionId),
            draft.DraftId,
            draft.Revision,
            content.Summary,
            content.ReplacementBehavior,
            content.ReplacementSource));
        var current = await hub.ReadDraftAsync(command.DraftId).WaitAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The Feature Draft does not exist in this Owner Scope.");
        if (!string.Equals(current.Status, "draft", StringComparison.Ordinal) || current.Revision != draft.Revision)
            throw new InvalidOperationException("The Draft Revision changed while producing the Suggested Change.");
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

    private static string PatchId(BrainOwnerId ownerId, FeatureDraft draft, string suggestionId)
    {
        var canonical = Encoding.UTF8.GetBytes($"digitalbrain.feature.patch\0{ownerId.Value}\0{draft.DraftId.Value}\0{draft.Revision}\0{suggestionId}");
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

internal sealed record FeatureSuggestionContent(
    string Summary,
    FeatureBehavior ReplacementBehavior,
    FeatureSourceSnapshot ReplacementSource);
