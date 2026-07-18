using System.Text.Json;
using System.Text.Json.Serialization;

namespace TripRadar.Server.Comms.Core.Convertors;

public class FlexibleDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                if (double.TryParse(reader.GetString(), out var stringValue))
                    return stringValue;
                return 0.0;
            case JsonTokenType.StartObject:
                // Skip the object and return 0.0 as default
                JsonDocument.ParseValue(ref reader);
                return 0.0;
            default:
                return 0.0;
        }
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}