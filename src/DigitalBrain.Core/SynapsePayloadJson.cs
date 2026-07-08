using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Core;

// System.Text.Json boxes any JSON value assigned to an object?-typed property as JsonElement unless told
// otherwise. Orleans has no codec for JsonElement, but has full native support for Dictionary, List, arrays,
// strings, and primitives boxed as object. This converter makes every ingestion site that deserializes into
// Dictionary<string, object?> (Gateway, test probes) produce only the latter — so nothing downstream ever
// needs to know JsonElement existed.
public static class SynapsePayloadJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ObjectValueConverter() }
    };

    private sealed class ObjectValueConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return Unwrap(document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);

        private static object? Unwrap(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? (object)integer : element.GetDouble(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => Unwrap(property.Value),
                StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(Unwrap).ToArray(),
            _ => element.GetString()
        };
    }
}
