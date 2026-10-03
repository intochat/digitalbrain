using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Postgres;

// Only this fixed projection is accepted. Filtering and aggregation use the existing parameterized
// query-plan compiler; neither an origin nor a physical table name is accepted from the caller.
internal sealed class PostgresAppLiveSource(Func<Task<PostgresAppTableResource>> resolve,
    Func<string, ILiveTableSource> source) : ILiveTableSource
{
    public static string Select(PostgresAppTableResource resource)
        => "SELECT " + string.Join(", ", resource.Table.Definition.Columns.Select(column => Quote(column.Name)))
            + " FROM " + Quote(resource.Table.Table);

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private async Task<ILiveTableSource> Authorized(string sql)
    {
        try
        {
            var resource = await resolve();
            if (!string.Equals(SupabaseQueryGuard.Normalize(sql), Select(resource), StringComparison.Ordinal))
            { throw new SupabaseTableValidationException("App table windows use the discovered table's fixed projection. Refine the returned window to filter or sort it."); }
            return source(resource.Origin);
        }
        catch (PostgresUnavailableException error) { throw new SupabaseTableSourceException(error.Message); }
        catch (Exception error) when (error is UnauthorizedAccessException or PostgresQueryException)
        { throw new SupabaseTableValidationException(error.Message); }
    }

    public async Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
        => await (await Authorized(sql)).DescribeAsync(sql, cancellationToken);

    public async Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
        => await (await Authorized(plan.BaseSql)).ExecutePlanAsync(plan, cancellationToken);

    public async Task<SupabaseTableAggregate> AggregateAsync(QueryPlan plan, string function, string columnId, CancellationToken cancellationToken = default)
        => await (await Authorized(plan.BaseSql)).AggregateAsync(plan, function, columnId, cancellationToken);
}
