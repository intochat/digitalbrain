using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;

namespace DigitalBrain.Kernel.Gateway;

// Surface-action payloads arrive from both Flutter (camelCase) and test/native callers (PascalCase).
// A case-insensitive view lets one set of key lookups serve both without silent misses.
public static class GatewayPayload
{
    public static Dictionary<string, object?> CaseInsensitive(Dictionary<string, object?>? source) =>
        new(source ?? [], StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, object?> PayloadProps(SynapseEnvelope request)
    {
        var payloadJson = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson, SynapsePayloadJson.Options) ?? [];
        return CaseInsensitive(raw);
    }
}
