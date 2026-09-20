namespace DigitalBrain.ClickHouse.Tables;

[GenerateSerializer, Alias("db.clickhouse.table-column")]
public sealed record ClickHouseTableColumn([property: Id(0)] string Id, [property: Id(1)] string Label, [property: Id(2)] string Type);

// Each cell is the cell's own JSON text (for example 42, "a" or null), so no foreign JSON
// serializer is needed at the grain boundary.
[GenerateSerializer, Alias("db.clickhouse.table-row")]
public sealed record ClickHouseTableRow([property: Id(0)] string Id, [property: Id(1)] IReadOnlyList<string> Cells);
