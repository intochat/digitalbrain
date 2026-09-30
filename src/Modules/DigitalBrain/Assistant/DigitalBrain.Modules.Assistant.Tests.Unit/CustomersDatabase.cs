using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;

namespace DigitalBrain.Assistant.Tests.Unit;

// A Supabase stand-in holding one table the scenarios fill; cells are JSON text, as on the wire.
internal sealed class CustomersDatabase : ISupabaseProvider
{
    private static readonly SupabaseColumn[] Columns = [new("id", "int4", "number"), new("name", "text", "text")];

    public string Table { get; set; } = "";

    public IReadOnlyList<SupabaseTableRow> Rows { get; set; } = [];

    public Task<SupabaseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
        => Task.FromResult(new SupabaseSchema("test", Table.Length == 0 ? [] : [new SupabaseTableInfo(Table, "table", Rows.Count, Columns)]));

    public Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SupabaseColumn>>(Columns);

    public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
        => Task.FromResult(new QueryPage(Rows, Rows.Count, Rows.Count));

    public Task<SupabaseTableAggregate> AggregateAsync(QueryPlan plan, string function, string columnId, CancellationToken cancellationToken = default)
        => Task.FromResult(new SupabaseTableAggregate(columnId, function, Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<SupabaseConnection> PingAsync(CancellationToken cancellationToken) => Task.FromResult(new SupabaseConnection(true, "test", "16.0", SupabaseModule.ProviderName));
}
