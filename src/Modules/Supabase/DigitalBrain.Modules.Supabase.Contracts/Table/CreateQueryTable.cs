namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("db.supabase.create-query-table")]
public sealed record CreateQueryTable(
    [property: Id(0)] string Title,
    [property: Id(1)] string Sql);