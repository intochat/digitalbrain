namespace DigitalBrain.Supabase.Tables;

// Function is one of count, sum, avg, min or max. ColumnId is empty for count. Value is the
// aggregate result as its own JSON text, matching the live-table cell wire contract.
[GenerateSerializer]
[Alias("supabase.read-table-aggregate")]
public sealed record ReadSupabaseTableAggregate(
    [property: Id(0)] string ColumnId,
    [property: Id(1)] string Function);

[GenerateSerializer]
[Alias("supabase.table-aggregate")]
public sealed record SupabaseTableAggregate(
    [property: Id(0)] string ColumnId,
    [property: Id(1)] string Function,
    [property: Id(2)] string Value);