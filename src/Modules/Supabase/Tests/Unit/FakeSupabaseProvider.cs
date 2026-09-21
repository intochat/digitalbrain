using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Modules.Supabase.Tests.Unit;

// A scripted stand-in for the Npgsql-backed provider so the neuron contracts and signals can be
// exercised without Docker, a database or the network. Cells are JSON text, as on the wire.
internal sealed class FakeSupabaseProvider : ISupabaseProvider
{
    public IReadOnlyList<SupabaseColumn> Described { get; set; } = [new("id", "int4", SupabaseTypeMap.Number)];

    public SupabaseQueryResult QueryResult { get; set; } =
        new([new("id", "int4", SupabaseTypeMap.Number)], [["7"]], 1, false, 0.5);

    public IReadOnlyList<SupabaseTableRow> PageRows { get; set; } = [new("row-0", ["7"])];

    public long Total { get; set; } = 1;

    public QueryPlan? LastPlan { get; private set; }

    public Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
        => Task.FromResult(QueryResult);

    public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
    {
        LastPlan = plan;
        return Task.FromResult(new QueryPage(PageRows, Total, Total));
    }

    public Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
        => Task.FromResult(Described);

    public Task<SupabaseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
        => Task.FromResult(new SupabaseSchema("test", []));

    public Task<SupabaseConnection> PingAsync(CancellationToken cancellationToken)
        => Task.FromResult(new SupabaseConnection(true, "test", "16.0", SupabaseModule.ProviderName));
}
