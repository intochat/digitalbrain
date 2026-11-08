using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalBrain;

// Walks a view's wire JSON: every object with a "key" and either a "kind" or an action member (emit, intent, resolve, onSubmit) is addressable, its "emit" object ({owner, signal, args}) is what it emits.
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
                if (element.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String && k.GetString() == key
                    && (Has(element, "kind", JsonValueKind.String) || CarriesAction(element))) { return element; }
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

    // A tab item or select option has no "kind" (only base-typed slots write the discriminator), nor has a card's child button: an action member marks the object.
    private static bool CarriesAction(JsonElement element)
        => Has(element, "emit", JsonValueKind.Object) || Has(element, "intent", JsonValueKind.Object) || Has(element, "resolve", JsonValueKind.Object) || Has(element, "onSubmit", JsonValueKind.Object);

    private static bool Has(JsonElement element, string name, JsonValueKind kind) => element.TryGetProperty(name, out var value) && value.ValueKind == kind;

    // The node's emit: owner and signal are strings, args optional; null when it has none.
    public static (string Owner, string Signal, JsonElement? Args)? EmitOf(JsonElement node)
    {
        // A field emits through onSubmit.
        if (!(node.TryGetProperty("emit", out var emit) && emit.ValueKind == JsonValueKind.Object)
            && !(node.TryGetProperty("onSubmit", out emit) && emit.ValueKind == JsonValueKind.Object)) { return null; }
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
