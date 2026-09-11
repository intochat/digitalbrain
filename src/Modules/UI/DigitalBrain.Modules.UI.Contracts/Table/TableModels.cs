using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.UI;

[GenerateSerializer, Alias("ui.table-column")]
public sealed record TableColumn([property: Id(0)] string Id, [property: Id(1)] string Label, [property: Id(2)] string Type);

[GenerateSerializer, Alias("ui.table-row")]
public sealed record TableRow([property: Id(0)] string Id, [property: Id(1)] IReadOnlyList<JsonElement> Cells);

[GenerateSerializer, Alias("ui.table-filter")]
[method: JsonConstructor]
public sealed record TableFilter([property: Id(0)] string ColumnId, [property: Id(1)] string Operator,
    [property: Id(2)] JsonElement Value)
{
    public TableFilter(string columnId, string @operator)
        : this(columnId, @operator, JsonSerializer.SerializeToElement<object?>(null)) { }
}

[GenerateSerializer, Alias("ui.table-sort")]
public sealed record TableSort([property: Id(0)] string ColumnId, [property: Id(1)] bool Descending = false);

[GenerateSerializer, Alias("ui.create-table")]
public sealed record CreateTable([property: Id(0)] string Title, [property: Id(1)] IReadOnlyList<TableColumn> Columns,
    [property: Id(2)] IReadOnlyList<TableRow> Rows);

[GenerateSerializer, Alias("ui.update-table-view")]
public sealed record UpdateTableView([property: Id(0)] long ExpectedRevision, [property: Id(1)] IReadOnlyList<TableFilter> Filters,
    [property: Id(2)] TableSort? Sort, [property: Id(3)] IReadOnlyList<string> VisibleColumns);

[GenerateSerializer, Alias("ui.table-summary")]
public sealed record TableSummary([property: Id(0)] string Id, [property: Id(1)] string Title, [property: Id(2)] long Revision);

[GenerateSerializer, Alias("ui.table-snapshot")]
public sealed record TableSnapshot(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] long Revision,
    [property: Id(3)] IReadOnlyList<TableColumn> Columns,
    [property: Id(4)] IReadOnlyList<TableRow> Rows,
    [property: Id(5)] IReadOnlyList<TableFilter> Filters,
    [property: Id(6)] TableSort? Sort,
    [property: Id(7)] IReadOnlyList<string> VisibleColumns,
    [property: Id(8)] int TotalRows,
    [property: Id(9)] int FilteredRows,
    [property: Id(10)] int Offset,
    [property: Id(11)] int Limit)
{
    public string Kind => "table";
}
