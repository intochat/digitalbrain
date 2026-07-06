using DigitalBrain.Runtime.Grpc;

namespace DigitalBrain.Kernel.Gateway;

// Surface-action payloads arrive from both Flutter (camelCase) and test/native callers (PascalCase).
// A case-insensitive view lets one set of key lookups serve both without silent misses.
public static class GatewayPayload
{
    public static Dictionary<string, object?> CaseInsensitive(Dictionary<string, object?>? source) =>
        new(source ?? new(), StringComparer.OrdinalIgnoreCase);

    // STJ deserializes JSON numbers/booleans as JsonElement when the target type is object?.
    // Unwrap them to CLR primitives so Signal consumers read int/long/double/bool/string directly.
    public static Dictionary<string, object?> NormalizeJsonProps(Dictionary<string, object?> raw)
    {
        var result = new Dictionary<string, object?>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            result[key] = value is System.Text.Json.JsonElement el ? UnwrapElement(el) : value;
        }
        return result;
    }

    public static Dictionary<string, object?> PayloadProps(SynapseEnvelope request)
    {
        var payloadJson = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson) ?? new();
        return NormalizeJsonProps(raw);
    }

    private static object? UnwrapElement(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
        System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        System.Text.Json.JsonValueKind.Object => el.GetRawText(),
        System.Text.Json.JsonValueKind.Array => el.GetRawText(),
        _ => el.GetString()
    };
}
