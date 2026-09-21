using DigitalBrain.ClickHouse.Query;

namespace DigitalBrain.ClickHouse.Tables;

// Durable state of a live table: the saved view, the SELECT behind it and the ClickHouse types
// its columns were derived from. Rows are always served live and never persisted.
[GenerateSerializer]
[Alias("db.clickhouse.table-state")]
internal sealed record ClickHouseTableState(
    [property: Id(0)] ClickHouseTableView? View,
    [property: Id(1)] string? BaseSql,
    [property: Id(2)] IReadOnlyList<ClickHouseColumn> SourceColumns);