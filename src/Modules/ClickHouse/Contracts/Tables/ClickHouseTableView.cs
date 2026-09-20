using System.Text.Json;

namespace DigitalBrain.ClickHouse.Tables;

// View carries columns, filters, sort, visible columns and revision; its rows are always served
// live from the saved SELECT and never persisted.
[GenerateSerializer, Alias("db.clickhouse.table-view")]
public sealed record ClickHouseTableView(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] long Revision,
    [property: Id(3)] IReadOnlyList<ClickHouseTableColumn> Columns,
    [property: Id(4)] IReadOnlyList<ClickHouseTableFilter> Filters,
    [property: Id(5)] ClickHouseTableSort? Sort,
    [property: Id(6)] IReadOnlyList<string> VisibleColumns);

[GenerateSerializer, Alias("db.clickhouse.table-snapshot")]
public sealed record ClickHouseTableSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] long Revision,
    [property: Id(3)] IReadOnlyList<ClickHouseTableColumn> Columns,
    [property: Id(4)] IReadOnlyList<ClickHouseTableRow> Rows,
    [property: Id(5)] IReadOnlyList<ClickHouseTableFilter> Filters,
    [property: Id(6)] ClickHouseTableSort? Sort,
    [property: Id(7)] IReadOnlyList<string> VisibleColumns,
    [property: Id(8)] int TotalRows,
    [property: Id(9)] int FilteredRows,
    [property: Id(10)] int Offset,
    [property: Id(11)] int Limit)
{
    public string Kind => "table";
}

[GenerateSerializer, Alias("db.clickhouse.table-summary")]
public sealed record ClickHouseTableSummary([property: Id(0)] string Id, [property: Id(1)] string Title, [property: Id(2)] long Revision);

[GenerateSerializer, Alias("db.clickhouse.read-table")]
public sealed record ReadClickHouseTable([property: Id(0)] int Offset = 0, [property: Id(1)] int Limit = 50);
