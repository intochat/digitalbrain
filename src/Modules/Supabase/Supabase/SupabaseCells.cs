using System.Globalization;
using System.Text.Json;

namespace DigitalBrain.Supabase;

// Read PostgreSQL's text protocol so unknown extension types and arbitrary-precision numbers
// are never forced through lossy CLR conversions. Text filters use this same server spelling.
internal static class SupabaseCells
{
    private const decimal MaxSafeInteger = 9007199254740991m;
    private static readonly JsonElement Null = JsonSerializer.SerializeToElement<object?>(null);

    public static JsonElement ToCell(string? value, string tableType)
    {
        if (value is null) { return Null; }
        if (tableType == SupabaseTypeMap.Boolean && value is "t" or "f")
        {
            return JsonSerializer.SerializeToElement(value == "t");
        }
        if (tableType == SupabaseTypeMap.Number &&
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            Math.Abs(number) <= MaxSafeInteger && SignificantDigits(value) <= 15 &&
            (number != 0 || SignificantDigits(value) == 0) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var original) && original == (double)number)
        {
            return JsonSerializer.SerializeToElement(number);
        }
        return JsonSerializer.SerializeToElement(value.Length > 4000 ? value[..4000] : value);
    }

    private static int SignificantDigits(string value)
    {
        var exponent = value.IndexOfAny(['e', 'E']);
        var mantissa = exponent < 0 ? value : value[..exponent];
        return mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('-', '+', '0').TrimEnd('0').Length;
    }
}
