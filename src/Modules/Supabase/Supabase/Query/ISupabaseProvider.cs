using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Supabase;

// The one live-table source contract: describe a read-only query, serve a compiled page, and
// compute an aggregate over the filtered view. The Supabase provider is the only implementation
// today; a connector plugs in by implementing this contract.
public interface ILiveTableSource
{
    Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken);

    Task<SupabaseTableAggregate> AggregateAsync(QueryPlan plan, string function, string columnId, CancellationToken cancellationToken = default);
}

// The interface between the neurons and a Supabase server.
public interface ISupabaseProvider : ILiveTableSource
{
    Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken);

    Task<SupabaseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken);

    Task<SupabaseConnection> PingAsync(CancellationToken cancellationToken);
}

// One page of a live table: the base query plus the ui view, compiled server-side.
public sealed record QueryPlan(
    string BaseSql,
    IReadOnlyList<SupabaseColumn> Columns,
    IReadOnlyList<SupabaseTableFilter> Filters,
    SupabaseTableSort? Sort,
    int Offset,
    int Limit);

public sealed record QueryPage(IReadOnlyList<SupabaseTableRow> Rows, long Total, long Filtered);