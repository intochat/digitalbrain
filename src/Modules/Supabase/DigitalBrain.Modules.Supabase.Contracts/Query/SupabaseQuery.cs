namespace DigitalBrain.Supabase;

[GenerateSerializer]
[Alias("db.supabase.query")]
public sealed record SupabaseQuery(
    [property: Id(0)] string Sql,
    [property: Id(1)] int MaxRows = SupabaseQuery.DefaultMaxRows)
{
    public const int DefaultMaxRows = 200;
    public const int MaxRowsLimit = 1000;
}
