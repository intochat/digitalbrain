namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("supabase.table-summary")]
public sealed record SupabaseTableSummary(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] long Revision);