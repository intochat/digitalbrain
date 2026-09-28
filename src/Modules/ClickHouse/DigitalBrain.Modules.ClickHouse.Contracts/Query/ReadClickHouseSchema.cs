namespace DigitalBrain.ClickHouse.Query;

[GenerateSerializer]
[Alias("db.clickhouse.read-schema")]
public sealed record ReadClickHouseSchema([property: Id(0)] string? Table = null);