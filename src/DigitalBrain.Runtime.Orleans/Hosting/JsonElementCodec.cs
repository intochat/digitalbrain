using System.Text.Json;
using System.Runtime.InteropServices;
using Orleans;
using Orleans.Serialization.Cloning;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>Orleans wire form of <see cref="JsonElement"/>: its UTF-8 JSON bytes, or null for an undefined element.</summary>
[GenerateSerializer, Alias("db.json-element")]
internal struct JsonElementSurrogate
{
    [Id(0)]
    public byte[]? Utf8;
}

/// <summary>
/// In-silo copies share the element: an encoded <see cref="JsonElement"/> owns its bytes and is immutable, so a local call
/// passes it as is instead of re-encoding it (one encoding pass per argument and result).
/// </summary>
[RegisterCopier]
internal sealed class JsonElementCopier : IDeepCopier<JsonElement>
{
    public JsonElement DeepCopy(JsonElement input, CopyContext context) => input;
}

[RegisterConverter]
internal sealed class JsonElementSurrogateConverter : IConverter<JsonElement, JsonElementSurrogate>
{
    public JsonElement ConvertFromSurrogate(in JsonElementSurrogate surrogate) => Parse(surrogate.Utf8);

    public JsonElementSurrogate ConvertToSurrogate(in JsonElement value) => new() { Utf8 = Utf8(value) };

    internal static byte[]? Utf8(JsonElement value) => value.ValueKind == JsonValueKind.Undefined ? null : JsonMarshal.GetRawUtf8Value(value).ToArray();

    internal static JsonElement Parse(byte[]? utf8) => utf8 is null ? default : JsonElement.Parse(utf8);
}
