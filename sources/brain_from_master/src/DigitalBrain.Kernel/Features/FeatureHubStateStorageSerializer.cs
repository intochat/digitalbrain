using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Kernel.Contracts;
using Orleans.Storage;

namespace DigitalBrain.Kernel.Features;

internal sealed class FeatureHubStateStorageSerializer(IGrainStorageSerializer inner) : IGrainStorageSerializer
{
    private const string LegacyDraftArrayType = "DigitalBrain.Kernel.Contracts.FeatureDraftProposal[], DigitalBrain.Kernel.Contracts";
    private const string LegacyDraftType = "DigitalBrain.Kernel.Contracts.FeatureDraftProposal, DigitalBrain.Kernel.Contracts";

    public BinaryData Serialize<T>(T value) => inner.Serialize(value);

    public T Deserialize<T>(BinaryData input)
    {
        if (typeof(T) != typeof(FeatureHubState) ||
            !input.ToString().Contains(LegacyDraftArrayType, StringComparison.Ordinal))
            return inner.Deserialize<T>(input);

        var root = JsonNode.Parse(
            input.ToString(),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false },
            new JsonDocumentOptions { MaxDepth = 64 }) as JsonObject
            ?? throw new JsonException("The Feature Hub state must be a JSON object.");
        var drafts = root["Drafts"] as JsonObject
            ?? throw new JsonException("The legacy Feature Hub Drafts collection is missing.");
        if (!string.Equals(String(drafts, "$type"), LegacyDraftArrayType, StringComparison.Ordinal))
            throw new JsonException("The legacy Feature Hub Drafts collection type is invalid.");
        var values = drafts["$values"] as JsonArray
            ?? throw new JsonException("The legacy Feature Hub Drafts values are missing.");
        var restored = values.Select(Restore).ToArray();
        root.Remove("Drafts");
        var state = inner.Deserialize<FeatureHubState>(BinaryData.FromString(root.ToJsonString()));
        return (T)(object)(state with { Drafts = restored, RequiresStorageRewrite = true });
    }

    private static FeatureDraft Restore(JsonNode? node)
    {
        var draft = node as JsonObject
            ?? throw new JsonException("A legacy Feature Draft must be a JSON object.");
        if (!string.Equals(String(draft, "$type"), LegacyDraftType, StringComparison.Ordinal))
            throw new JsonException("A legacy Feature Draft type is invalid.");
        var createdAtText = String(draft, "CreatedAt");
        if (!DateTimeOffset.TryParse(
                createdAtText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAt))
            throw new JsonException("A legacy Feature Draft creation time is invalid.");
        return FeatureDraft.RestoreLegacy(
            String(draft, "ProposalId"),
            String(draft, "OperationId"),
            String(draft, "Goal"),
            String(draft, "Status"),
            createdAt);
    }

    private static string String(JsonObject value, string name) =>
        value[name]?.GetValue<string>() is { } result
            ? result
            : throw new JsonException($"The legacy Feature Draft {name} value is missing.");
}
