namespace DigitalBrain.ClickHouse;

// TableType is the ui table column domain: text, number, date or boolean.
[GenerateSerializer]
[Alias("db.clickhouse.column")]
public sealed record ClickHouseColumn(
    [property: Id(0)] string Name,
    [property: Id(1)] string ClickHouseType,
    [property: Id(2)] string TableType);
