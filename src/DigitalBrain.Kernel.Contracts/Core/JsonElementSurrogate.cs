using System.Text.Json;

namespace DigitalBrain.Kernel.Contracts;

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.JsonElementSurrogate")]
public struct JsonElementSurrogate
{
    [Id(0)]
    public string Json;
}

[RegisterConverter]
public sealed class JsonElementSurrogateConverter : IConverter<JsonElement, JsonElementSurrogate>
{
    public JsonElement ConvertFromSurrogate(in JsonElementSurrogate surrogate)
    {
        using var document = JsonDocument.Parse(surrogate.Json);
        return document.RootElement.Clone();
    }

    public JsonElementSurrogate ConvertToSurrogate(in JsonElement value) =>
        new() { Json = value.GetRawText() };
}
