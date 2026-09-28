namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("supabase.update-table-view")]
public sealed record UpdateSupabaseTableView(
    [property: Id(0)] long ExpectedRevision,
    [property: Id(1)] IReadOnlyList<SupabaseTableFilter> Filters,
    [property: Id(2)] SupabaseTableSort? Sort,
    [property: Id(3)] IReadOnlyList<string> VisibleColumns);