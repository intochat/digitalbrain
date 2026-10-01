namespace DigitalBrain.Supabase;

// TableType is the UI column domain; PostgreSqlType preserves the server's database type.
[GenerateSerializer]
[Alias("db.supabase.column")]
public sealed record SupabaseColumn(
    [property: Id(0)] string Name,
    [property: Id(1)] string PostgreSqlType,
    [property: Id(2)] string TableType);
