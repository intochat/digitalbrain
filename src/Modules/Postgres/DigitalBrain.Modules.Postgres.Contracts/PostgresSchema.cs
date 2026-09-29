namespace DigitalBrain.Postgres;

[GenerateSerializer]
[Alias("db.postgres.table-info")]
public sealed record PostgresTableInfo(
    [property: Id(0)] string Name,
    [property: Id(1)] string Kind,
    [property: Id(2)] long? TotalRows,
    [property: Id(3)] IReadOnlyList<PostgresColumn> Columns);

[GenerateSerializer]
[Alias("db.postgres.schema")]
public sealed record PostgresSchema(
    [property: Id(0)] string Database,
    [property: Id(1)] IReadOnlyList<PostgresTableInfo> Tables);