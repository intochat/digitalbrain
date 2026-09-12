using DigitalBrain.UI;

namespace DigitalBrain.ClickHouse;

// View carries columns, filters, sort, visible columns and revision; its Rows are always empty
// and its counts are zero. Rows are served live from BaseSql and never persisted.
[GenerateSerializer]
[Alias("db.clickhouse.table-state")]
public sealed record ClickHouseTableState(
    [property: Id(0)] TableSnapshot? View,
    [property: Id(1)] string? BaseSql,
    [property: Id(2)] IReadOnlyList<TableOperationResult> Operations);
