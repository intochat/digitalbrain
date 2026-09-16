using DigitalBrain.Flutter;

namespace DigitalBrain.ClickHouse;

// The interface between the neurons and a ClickHouse server.
internal interface IClickHouseProvider
{
    string ProviderName { get; }

    Task<ClickHouseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken);

    Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClickHouseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken);

    Task<ClickHouseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken);

    Task<ClickHouseConnection> PingAsync(CancellationToken cancellationToken);
}

// One page of a live table: the base query plus the ui view, compiled server-side.
internal sealed record QueryPlan(
    string BaseSql,
    IReadOnlyList<ClickHouseColumn> Columns,
    IReadOnlyList<TableFilter> Filters,
    TableSort? Sort,
    int Offset,
    int Limit);

internal sealed record QueryPage(IReadOnlyList<TableRow> Rows, long Total, long Filtered);
