namespace DigitalBrain.Supabase.Tables;

// Value is a filter operand as JSON text (for example 42, "a" or null); null checks carry the
// text "null" and no value. JSON text keeps the type free of a foreign JSON serializer.
[GenerateSerializer]
[Alias("supabase.table-filter")]
public sealed record SupabaseTableFilter(
    [property: Id(0)] string ColumnId,
    [property: Id(1)] string Operator,
    [property: Id(2)] string Value)
{
    public SupabaseTableFilter(string columnId, string @operator)
        : this(columnId, @operator, "null") { }
}