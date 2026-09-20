using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Supabase;

// The interface between the neurons and a Supabase server.
public interface ISupabaseProvider
{
    Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken);

    Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken);

    Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken);

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
