namespace DigitalBrain.Supabase.Tables;

// Each cell is the cell's own JSON text (for example 42, "a" or null), so no foreign JSON
// serializer is needed at the grain boundary.
[GenerateSerializer]
[Alias("supabase.table-row")]
public sealed record SupabaseTableRow(
    [property: Id(0)] string Id,
    [property: Id(1)] IReadOnlyList<string> Cells);