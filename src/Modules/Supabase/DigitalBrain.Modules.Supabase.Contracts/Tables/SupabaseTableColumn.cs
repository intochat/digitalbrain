namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("supabase.table-column")]
public sealed record SupabaseTableColumn(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] string Type);