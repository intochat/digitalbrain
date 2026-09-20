namespace DigitalBrain.Supabase;

[GenerateSerializer]
[Alias("db.supabase.table-info")]
public sealed record SupabaseTableInfo(
    [property: Id(0)] string Name,
    [property: Id(1)] string Kind,
    [property: Id(2)] long? TotalRows,
    [property: Id(3)] IReadOnlyList<SupabaseColumn> Columns);

[GenerateSerializer]
[Alias("db.supabase.schema")]
public sealed record SupabaseSchema(
    [property: Id(0)] string Database,
    [property: Id(1)] IReadOnlyList<SupabaseTableInfo> Tables);