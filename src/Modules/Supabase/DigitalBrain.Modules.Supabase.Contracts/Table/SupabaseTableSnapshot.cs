namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("supabase.table-snapshot")]
public sealed record SupabaseTableSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] long Revision,
    [property: Id(3)] IReadOnlyList<SupabaseTableColumn> Columns,
    [property: Id(4)] IReadOnlyList<SupabaseTableRow> Rows,
    [property: Id(5)] IReadOnlyList<SupabaseTableFilter> Filters,
    [property: Id(6)] SupabaseTableSort? Sort,
    [property: Id(7)] IReadOnlyList<string> VisibleColumns,
    [property: Id(8)] int TotalRows,
    [property: Id(9)] int FilteredRows,
    [property: Id(10)] int Offset,
    [property: Id(11)] int Limit)
{
    public string Kind => "table";
}