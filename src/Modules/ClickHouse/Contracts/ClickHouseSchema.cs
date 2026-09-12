namespace DigitalBrain.ClickHouse;

[GenerateSerializer]
[Alias("db.clickhouse.table-info")]
public sealed record ClickHouseTableInfo(
    [property: Id(0)] string Name,
    [property: Id(1)] string Engine,
    [property: Id(2)] long? TotalRows,
    [property: Id(3)] IReadOnlyList<ClickHouseColumn> Columns);

[GenerateSerializer]
[Alias("db.clickhouse.schema")]
public sealed record ClickHouseSchema(
    [property: Id(0)] string Database,
    [property: Id(1)] IReadOnlyList<ClickHouseTableInfo> Tables);
