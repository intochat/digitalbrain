using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Supabase;

// View carries columns, filters, sort, visible columns and revision; its Rows are always empty
// and its counts are zero. Rows are served live from BaseSql and never persisted. SourceColumns
// keeps the Supabase types the view's columns were derived from.
[GenerateSerializer, Alias("supabase.table-state")]
internal sealed record SupabaseTableState(
    [property: Id(0)] SupabaseTableSnapshot? View,
    [property: Id(1)] string? BaseSql,
    [property: Id(2)] IReadOnlyList<SupabaseColumn> SourceColumns)
{
    [Id(3)] public string? CreationOperation { get; init; }
    [Id(4)] public CreateQueryTable? CreationRequest { get; init; }
    internal static SupabaseTableState Empty { get; } = new(null, null, []);
}