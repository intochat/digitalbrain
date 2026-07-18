using System.Text.Json;
using System.Text.Json.Serialization;
using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed class NeuronIdJsonConverter : JsonConverter<NeuronId>
{
    public override NeuronId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? throw new JsonException("NeuronId cannot be null");
        return NeuronId.From(raw);
    }

    public override void Write(Utf8JsonWriter writer, NeuronId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
