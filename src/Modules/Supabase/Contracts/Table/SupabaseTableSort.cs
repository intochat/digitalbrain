namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("supabase.table-sort")]
public sealed record SupabaseTableSort(
    [property: Id(0)] string ColumnId,
    [property: Id(1)] bool Descending = false);