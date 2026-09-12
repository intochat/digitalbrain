using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DigitalBrain.ClickHouse;

// Turns driver values into the JSON scalars the ui table understands. Numbers obey TablePolicy's
// rule (at most 15 significant digits, magnitude at most 2^53 - 1) or fall back to text; dates
// are yyyy-MM-dd; everything else is a string, JSON-serialised when it is structured.
internal static class ClickHouseCells
{
    public const int MaxTextLength = 4000;
    private const decimal MaxSafeInteger = 9007199254740991m;
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);
    private static readonly JsonSerializerOptions StructuredJson = new(JsonSerializerDefaults.General);

    public static JsonElement ToCell(object? value, string tableType)
    {
        if (value is null or DBNull)
        {
            return Null;
        }

        return tableType switch
        {
            ClickHouseTypeMap.Number => NumberCell(value),
            ClickHouseTypeMap.Boolean => value is bool flag ? JsonSerializer.SerializeToElement(flag) : TextCell(value),
            ClickHouseTypeMap.Date => DateCell(value),
            _ => TextCell(value),
        };
    }

    private static JsonElement NumberCell(object value)
        => value switch
        {
            float single => FloatingCell(single),
            double floating => FloatingCell(floating),
            decimal exact => DecimalCell(exact),
            byte or sbyte or short or ushort or int or uint or long => DecimalCell(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
            ulong unsigned => unsigned > (ulong)MaxSafeInteger ? TextCell(value) : DecimalCell(unsigned),
            BigInteger big => BigInteger.Abs(big) > new BigInteger(MaxSafeInteger) ? TextCell(value) : DecimalCell((decimal)big),
            _ => TextCell(value),
        };

    private static JsonElement FloatingCell(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > (double)MaxSafeInteger)
        {
            return TextCell(value);
        }

        // Aggregates such as avg() carry noise beyond 15 digits; round to what the table can filter on.
        var rounded = double.Parse(value.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return JsonSerializer.SerializeToElement(rounded);
    }

    private static JsonElement DecimalCell(decimal value)
    {
        if (Math.Abs(value) > MaxSafeInteger || SignificantDigits(value) > 15)
        {
            return TextCell(value);
        }

        return JsonSerializer.SerializeToElement(value);
    }

    private static int SignificantDigits(decimal value)
    {
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture).Replace(".", "", StringComparison.Ordinal).Trim('0');
        return digits.Length;
    }

    private static JsonElement DateCell(object value)
        => value switch
        {
            DateOnly date => JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            DateTime date => JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            DateTimeOffset date => JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            _ => TextCell(value),
        };

    private static JsonElement TextCell(object value)
    {
        var text = value switch
        {
            string plain => plain,
            DateTime moment => moment.ToString(moment.Kind == DateTimeKind.Utc ? "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
            DateTimeOffset moment => moment.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            bool flag => flag ? "true" : "false",
            byte[] bytes => Convert.ToBase64String(bytes),
            JsonNode node => node.ToJsonString(),
            JsonElement element => element.GetRawText(),
            ITuple tuple => Structured(Enumerable.Range(0, tuple.Length).Select(index => tuple[index]).ToArray()),
            IEnumerable structured => Structured(structured),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
        if (text.Length > MaxTextLength)
        {
            text = text[..MaxTextLength];
        }

        return JsonSerializer.SerializeToElement(text);
    }

    private static string Structured(IEnumerable value)
    {
        try
        {
            // Serialise by runtime type: a dictionary must become an object, not a list of pairs.
            return JsonSerializer.Serialize<object>(value, StructuredJson);
        }
        catch (NotSupportedException)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
