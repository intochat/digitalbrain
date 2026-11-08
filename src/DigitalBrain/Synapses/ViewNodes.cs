using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.UI;

namespace DigitalBrain;

// Walks a view's node tree by reflection, so every node type that carries a Key and an Emit is activatable without a list here.
internal static class ViewNodes
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Children = new();

    // The first node, depth first, whose Key is the key.
    public static Node? Find(Node root, string key)
    {
        var pending = new Stack<Node>([root]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (string.Equals(node.GetType().GetProperty("Key")?.GetValue(node) as string, key, StringComparison.Ordinal)) { return node; }
            var found = new List<Node>();
            foreach (var property in ChildProperties(node.GetType()))
            {
                switch (property.GetValue(node))
                {
                    case Node child: found.Add(child); break;
                    case IEnumerable many and not string: found.AddRange(many.OfType<Node>()); break;
                }
            }
            for (var i = found.Count - 1; i >= 0; i--) { pending.Push(found[i]); }
        }

        return null;
    }

    public static Emit? EmitOf(Node node) => node.GetType().GetProperty("Emit")?.GetValue(node) as Emit;

    // The emit's args (a JSON object, or none) with the field value added as "value".
    public static JsonElement Payload(JsonElement? args, string? fieldValue)
    {
        if (fieldValue is null && args is { } only) { return only.Clone(); }
        var payload = args is { ValueKind: JsonValueKind.Object } given ? JsonObject.Create(given) ?? [] : [];
        if (fieldValue is not null) { payload["value"] = fieldValue; }
        return JsonSerializer.SerializeToElement(payload);
    }

    private static PropertyInfo[] ChildProperties(Type type) => Children.GetOrAdd(type, t =>
        [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0
            && (typeof(Node).IsAssignableFrom(p.PropertyType) || (p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))))]);
}
