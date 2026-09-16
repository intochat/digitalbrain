using DigitalBrain.Flutter;

namespace DigitalBrain.Supabase;

// The seam between the neurons and a Supabase server; the fake implements it in memory.
internal interface ISupabaseProvider
{
    string ProviderName { get; }

    Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken);

    Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken);

    Task<SupabaseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken);

    Task<SupabaseConnection> PingAsync(CancellationToken cancellationToken);
}

// One page of a live table: the base query plus the ui view, compiled server-side.
internal sealed record QueryPlan(
    string BaseSql,
    IReadOnlyList<SupabaseColumn> Columns,
    IReadOnlyList<TableFilter> Filters,
    TableSort? Sort,
    int Offset,
    int Limit);

internal sealed record QueryPage(IReadOnlyList<TableRow> Rows, long Total, long Filtered);
