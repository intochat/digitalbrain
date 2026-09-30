namespace DigitalBrain.Postgres;

// Each cell is the cell's own JSON text (for example 42, "a" or null); JSON travels as text so
// the cell type needs no foreign serializer at the grain boundary.
[GenerateSerializer]
[Alias("db.postgres.query-result")]
public sealed record PostgresQueryResult(
    [property: Id(0)] IReadOnlyList<PostgresColumn> Columns,
    [property: Id(1)] IReadOnlyList<IReadOnlyList<string>> Rows,
    [property: Id(2)] long RowCount,
    [property: Id(3)] bool Truncated,
    [property: Id(4)] double ElapsedMs);
