using System.Text.Json;
using System.Text.Json.Serialization;
using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed class DomainIdJsonConverter : JsonConverter<DomainId>
{
    public override DomainId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? throw new JsonException("DomainId cannot be null");
        return DomainId.From(raw);
    }

    public override void Write(Utf8JsonWriter writer, DomainId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
