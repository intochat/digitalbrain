namespace DigitalBrain.ClickHouse.Tables;

// Value is a filter operand as JSON text (for example 42, "a" or null); null checks carry the
// text "null" and no value. JSON text keeps the type free of a foreign JSON serializer.
[GenerateSerializer]
[Alias("db.clickhouse.table-filter")]
public sealed record ClickHouseTableFilter([property: Id(0)] string ColumnId, [property: Id(1)] string Operator,
    [property: Id(2)] string Value)
{
    public ClickHouseTableFilter(string columnId, string @operator)
        : this(columnId, @operator, "null") { }
}

[GenerateSerializer]
[Alias("db.clickhouse.table-sort")]
public sealed record ClickHouseTableSort([property: Id(0)] string ColumnId, [property: Id(1)] bool Descending = false);

[GenerateSerializer]
[Alias("db.clickhouse.update-table-view")]
public sealed record UpdateClickHouseTableView([property: Id(0)] long ExpectedRevision,
    [property: Id(1)] IReadOnlyList<ClickHouseTableFilter> Filters, [property: Id(2)] ClickHouseTableSort? Sort,
    [property: Id(3)] IReadOnlyList<string> VisibleColumns);