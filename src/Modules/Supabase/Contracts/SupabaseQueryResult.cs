using System.Text.Json;

namespace DigitalBrain.Supabase;

[GenerateSerializer]
[Alias("db.supabase.query-result")]
public sealed record SupabaseQueryResult(
    [property: Id(0)] IReadOnlyList<SupabaseColumn> Columns,
    [property: Id(1)] IReadOnlyList<IReadOnlyList<JsonElement>> Rows,
    [property: Id(2)] long RowCount,
    [property: Id(3)] bool Truncated,
    [property: Id(4)] double ElapsedMs);
