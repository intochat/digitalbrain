using System.Globalization;
using System.Text.Json;

namespace DigitalBrain.Supabase;

// Read PostgreSQL's text protocol so unknown extension types and arbitrary-precision numbers
// are never forced through lossy CLR conversions. Text filters use this same server spelling.
// Cells travel as their own JSON text so no foreign serializer is needed at the grain boundary.
internal static class SupabaseCells
{
    private const decimal MaxSafeInteger = 9007199254740991m;

    public static string ToCell(string? value, string tableType)
    {
        if (value is null) { return "null"; }
        if (tableType == SupabaseTypeMap.Boolean && value is "t" or "f")
        {
            return value == "t" ? "true" : "false";
        }
        if (tableType == SupabaseTypeMap.Number &&
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            Math.Abs(number) <= MaxSafeInteger && SignificantDigits(value) <= 15 &&
            (number != 0 || SignificantDigits(value) == 0) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var original) && original == (double)number)
        {
            return JsonSerializer.SerializeToElement(number).GetRawText();
        }
        return JsonSerializer.SerializeToElement(value.Length > 4000 ? value[..4000] : value).GetRawText();
    }

    private static int SignificantDigits(string value)
    {
        var exponent = value.IndexOfAny(['e', 'E']);
        var mantissa = exponent < 0 ? value : value[..exponent];
        return mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('-', '+', '0').TrimEnd('0').Length;
    }
}