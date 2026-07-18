using System.Text.Json;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Planning;

internal sealed record DraftedNeuron(
    string FeatureText,
    string StepsCode,
    string ImplCode,
    string DisplayName,
    string Icon,
    IReadOnlyList<string> RequiresCapabilities,
    string InvocationSynapseType,
    string InvocationPayloadJson,
    string ResponseSynapseType)
{
    public static DraftedNeuron ParseFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var invocation = root.GetProperty("invocation");
        var requires = root.TryGetProperty("requires", out var req)
            ? req.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : Array.Empty<string>();
        return new DraftedNeuron(
            FeatureText:           root.GetProperty("feature").GetString() ?? "",
            StepsCode:             root.GetProperty("steps").GetString() ?? "",
            ImplCode:              root.GetProperty("impl").GetString() ?? "",
            DisplayName:           root.GetProperty("displayName").GetString() ?? "",
            Icon:                  root.GetProperty("icon").GetString() ?? "default",
            RequiresCapabilities:  requires,
            InvocationSynapseType: invocation.GetProperty("synapseTypeName").GetString() ?? "",
            InvocationPayloadJson: invocation.GetProperty("payloadJson").GetString() ?? "{}",
            ResponseSynapseType:   root.TryGetProperty("responseSynapseType", out var r)
                                       ? r.GetString() ?? ""
                                       : "");
    }
}
