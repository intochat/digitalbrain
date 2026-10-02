namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("supabase.table-snapshot")]
public sealed record SupabaseTableSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] long Revision,
    [property: Id(3)] SupabaseTableColumn[] Columns,
    [property: Id(4)] SupabaseTableRow[] Rows,
    [property: Id(5)] SupabaseTableFilter[] Filters,
    [property: Id(6)] SupabaseTableSort? Sort,
    [property: Id(7)] string[] VisibleColumns,
    [property: Id(8)] int TotalRows,
    [property: Id(9)] int FilteredRows,
    [property: Id(10)] int Offset,
    [property: Id(11)] int Limit)
{
    [Id(12)] public string Source { get; init; } = "supabase";
    public string Kind => "table";
}
