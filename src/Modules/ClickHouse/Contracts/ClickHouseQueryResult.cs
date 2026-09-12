using System.Text.Json;

namespace DigitalBrain.ClickHouse;

[GenerateSerializer]
[Alias("db.clickhouse.query-result")]
public sealed record ClickHouseQueryResult(
    [property: Id(0)] IReadOnlyList<ClickHouseColumn> Columns,
    [property: Id(1)] IReadOnlyList<IReadOnlyList<JsonElement>> Rows,
    [property: Id(2)] long RowCount,
    [property: Id(3)] bool Truncated,
    [property: Id(4)] double ElapsedMs);
