using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalBrain;

// Walks a view's wire JSON: every node (an object with a "kind") with a "key" is addressable, its "emit" object ({owner, signal, args}) is what it emits.
internal static class ViewNodes
{
    // The full name of the view model's root record, which the core does not reference.
    public const string ViewType = "DigitalBrain.UI.View";

    // The first node, depth first, whose "key" is the key; emit and args objects are not searched.
    public static JsonElement? Find(JsonElement root, string key)
    {
        var pending = new Stack<JsonElement>([root]);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                    && element.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() == key) { return element; }
                // An emit (and its args) is data, not view: a "key" inside it names no node.
                foreach (var property in element.EnumerateObject().Reverse().Where(p => p.Name is not ("emit" or "args"))) { pending.Push(property.Value); }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray().Reverse()) { pending.Push(item); }
            }
        }

        return null;
    }

    // The node's emit: owner and signal are strings, args optional; null when it has none.
    public static (string Owner, string Signal, JsonElement? Args)? EmitOf(JsonElement node)
    {
        if (!node.TryGetProperty("emit", out var emit) || emit.ValueKind != JsonValueKind.Object) { return null; }
        if (!emit.TryGetProperty("owner", out var owner) || owner.ValueKind != JsonValueKind.String
            || !emit.TryGetProperty("signal", out var signal) || signal.ValueKind != JsonValueKind.String) { return null; }
        JsonElement? args = emit.TryGetProperty("args", out var a) && a.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? a : null;
        return (owner.GetString()!, signal.GetString()!, args);
    }

    // The emit's args (a JSON object, or none) with the field value added as "value".
    public static JsonElement Payload(JsonElement? args, string? fieldValue)
    {
        if (fieldValue is null && args is { } only) { return only.Clone(); }
        var payload = args is { ValueKind: JsonValueKind.Object } given ? JsonObject.Create(given) ?? [] : [];
        if (fieldValue is not null) { payload["value"] = fieldValue; }
        return JsonSerializer.SerializeToElement(payload);
    }
}
