namespace DigitalBrain.ClickHouse.Tables;

[GenerateSerializer]
[Alias("db.clickhouse.create-query-table")]
public sealed record CreateQueryTable(
    [property: Id(0)] string Title,
    [property: Id(1)] string Sql);