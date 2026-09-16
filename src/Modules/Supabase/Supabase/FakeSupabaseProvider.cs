using System.Text.Json;
using DigitalBrain.UI;

namespace DigitalBrain.Supabase;

// An explicit fake: scripts exact query results instead of pretending to interpret PostgreSQL.
internal sealed class FakeSupabaseProvider : ISupabaseProvider
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SupabaseQueryResult> _queries = new(StringComparer.Ordinal);

    public FakeSupabaseProvider()
    {
        Script("SELECT 1 AS value", new([new("value", "integer", "number")], [[JsonSerializer.SerializeToElement(1)]], 1, false, 0));
    }

    public string ProviderName => "Fake";

    public void Script(string sql, SupabaseQueryResult result) => _queries[sql] = result;

    public Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SupabaseQueryGuard.Validate(sql);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRows, SupabaseQuery.MaxRowsLimit);
        var result = Resolve(sql);
        var rows = result.Rows.Take(maxRows).ToArray();
        return Task.FromResult(result with { Rows = rows, RowCount = rows.Length, Truncated = result.Truncated || result.Rows.Count > maxRows });
    }

    public Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SupabaseQueryGuard.Validate(sql);
        return Task.FromResult(Resolve(sql).Columns);
    }

    public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SupabaseQueryGuard.Validate(plan.BaseSql);
        TablePolicy.ValidatePage(plan.Offset, plan.Limit);
        var result = Resolve(plan.BaseSql);
        var columns = result.Columns.Select(c => new TableColumn(c.Name, c.Name, c.TableType)).ToArray();
        var rows = result.Rows.Select((cells, index) => new TableRow($"row-{index}", cells)).ToArray();
        var view = new TableSnapshot("fake", "fake", 1, columns, rows, plan.Filters, plan.Sort,
            columns.Select(c => c.Id).ToArray(), rows.Length, rows.Length, 0, 50);
        var filtered = TablePolicy.Apply(view);
        var page = filtered.Skip(plan.Offset).Take(plan.Limit).Select((row, index) => new TableRow($"row-{plan.Offset + index}", row.Cells)).ToArray();
        return Task.FromResult(new QueryPage(page, rows.Length, filtered.Count));
    }

    public Task<SupabaseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SupabaseSchema("fake", []));
    }

    public Task<SupabaseConnection> PingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SupabaseConnection(true, "fake", "fake", ProviderName));
    }

    private SupabaseQueryResult Resolve(string sql) => _queries.TryGetValue(sql, out var result)
        ? result
        : throw new SupabaseQueryException("The fake has no result for this SQL. Register an exact result with Script(sql, result).");
}
