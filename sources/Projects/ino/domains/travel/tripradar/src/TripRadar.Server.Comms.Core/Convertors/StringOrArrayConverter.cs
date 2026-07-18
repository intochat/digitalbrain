using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TripRadar.Server.Comms.Core.Convertors;

public class StringOrArrayConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Number:
                return reader.GetDouble().ToString(CultureInfo.InvariantCulture);

            case JsonTokenType.True:
                return "true";

            case JsonTokenType.False:
                return "false";

            case JsonTokenType.StartArray:
            {
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        break;

                    switch (reader.TokenType)
                    {
                        case JsonTokenType.String:
                        {
                            var value = reader.GetString();
                            if (value != null) list.Add(value);
                            break;
                        }
                        case JsonTokenType.StartObject:
                        {
                            using var doc = JsonDocument.ParseValue(ref reader);
                            list.Add(doc.RootElement.GetRawText());
                            break;
                        }
                        case JsonTokenType.Number:
                            list.Add(reader.GetDouble().ToString(CultureInfo.InvariantCulture));
                            break;
                        case JsonTokenType.True:
                            list.Add("true");
                            break;
                        case JsonTokenType.False:
                            list.Add("false");
                            break;
                        case JsonTokenType.None:
                        case JsonTokenType.EndObject:
                        case JsonTokenType.StartArray:
                        case JsonTokenType.EndArray:
                        case JsonTokenType.PropertyName:
                        case JsonTokenType.Comment:
                        case JsonTokenType.Null:
                            break;
                        default:
                            throw new JsonException($"Unsupported JSON token type '{reader.TokenType}' in string array conversion.");
                    }
                }

                return list.Count > 0 ? string.Join(", ", list) : null;
            }

            case JsonTokenType.StartObject:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return doc.RootElement.GetRawText();
            }

            case JsonTokenType.None:
            case JsonTokenType.EndObject:
            case JsonTokenType.EndArray:
            case JsonTokenType.PropertyName:
            case JsonTokenType.Comment:
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteStringValue(value);
                break;
        }
    }
}

