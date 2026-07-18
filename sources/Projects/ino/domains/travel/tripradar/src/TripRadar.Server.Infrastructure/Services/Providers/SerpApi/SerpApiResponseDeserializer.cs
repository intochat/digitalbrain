using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TripRadar.Server.Infrastructure.Services.Providers.SerpApi;

internal static class SerpApiResponseDeserializer
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new ObjectToInferredTypesConverter() }
    };

    public static TResponse? Deserialize<TResponse>(string? json) => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<TResponse>(json, _jsonOptions);

    private sealed class ObjectToInferredTypesConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.String => reader.TryGetDateTime(out var dateTime)
                    ? dateTime
                    : reader.GetString(),
                JsonTokenType.Number => ReadNumber(ref reader),
                JsonTokenType.StartObject => JsonSerializer.Deserialize<Dictionary<string, object?>>(ref reader, options),
                JsonTokenType.StartArray => JsonSerializer.Deserialize<List<object?>>(ref reader, options),
                JsonTokenType.Null => null,
                _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
            };

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, value.GetType(), options);

        private static object ReadNumber(ref Utf8JsonReader reader)
        {
            if (reader.TryGetInt64(out var longValue))
            {
                return longValue;
            }

            if (reader.TryGetDecimal(out var decimalValue))
            {
                return decimalValue;
            }

            return reader.GetDouble();
        }
    }
}
