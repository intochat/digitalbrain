namespace DigitalBrain.ClickHouse;

[GenerateSerializer]
[Alias("db.clickhouse.connection")]
public sealed record ClickHouseConnection(
    [property: Id(0)] bool Connected,
    [property: Id(1)] string Database,
    [property: Id(2)] string? ServerVersion,
    [property: Id(3)] string Provider);
