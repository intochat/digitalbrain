using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Supabase;

internal static class SupabaseTypeMap
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Boolean = "boolean";

    public static string ToTableType(string type) => type switch
    {
        Text or Number or Date or Boolean => type,
        "bool" => Boolean,
        "int2" or "int4" or "int8" or "smallint" or "integer" or "bigint" or
        "float4" or "float8" or "real" or "double precision" or "numeric" or "decimal" or "money" => Number,
        _ => Text,
    };

    // Types the live table through the P1.1 semantic catalog so sensitivity and redaction are the
    // catalog's, not a second table of rules.
    public static FieldKind ToFieldKind(string tableType) => ToTableType(tableType) switch
    {
        Boolean => FieldKind.Boolean,
        Date => FieldKind.Date,
        Number => FieldKind.Number,
        _ => FieldKind.PlainText,
    };
}