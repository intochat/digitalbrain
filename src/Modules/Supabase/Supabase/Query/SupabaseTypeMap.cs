namespace DigitalBrain.Supabase;

internal static class SupabaseTypeMap
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Boolean = "boolean";

    public static string ToTableType(string type) => type switch
    {
        "bool" or "boolean" => Boolean,
        "date" => Date,
        "int2" or "int4" or "int8" or "smallint" or "integer" or "bigint" or
        "float4" or "float8" or "real" or "double precision" or "numeric" or "decimal" or "money" => Number,
        _ => Text,
    };
}