namespace DigitalBrain.ClickHouse;

[GenerateSerializer]
[Alias("db.clickhouse.query")]
public sealed record ClickHouseQuery(
    [property: Id(0)] string Sql,
    [property: Id(1)] int MaxRows = ClickHouseQuery.DefaultMaxRows)
{
    public const int DefaultMaxRows = 200;
    public const int MaxRowsLimit = 1000;
}
