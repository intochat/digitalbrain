using DigitalBrain.Postgres;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Supabase.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresTableFacts
{
    [Fact]
    public async Task OpenReadRefineAndAggregateUseTheSelectedSourceOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var postgres = new Source("postgres");
        var supabase = new Source("supabase");
        await using var brain = await UnitTest.Create().WithModule<DigitalBrain.Flutter.FlutterModule>().WithModule<PostgresModule>()
            .ConfigureSilo(silo =>
            {
                silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
                silo.Services.AddKeyedSingleton<ILiveTableSource>("postgres", postgres);
                silo.Services.AddSingleton<ILiveTableSource>(supabase);
            }).StartAsync(ct);
        var windows = new LiveTableWindows(brain);
        var opened = await windows.OpenAsync("workspace-pg", "run", "call", "Postgres customers", "select name from customers", ct, "postgres");
        var table = brain.Get<ISupabaseTable>(opened.TableId);
        Assert.Equal("postgres", opened.Source);
        Assert.Equal("postgres", (await table.ReadSummary())!.Source);
        Assert.Equal("\"postgres\"", Assert.Single(Assert.Single((await table.Read(new(0, 25)))!.Rows).Cells));
        await windows.RefineAsync("workspace-pg", opened.TableId, [], null, null, ct, "postgres");
        await windows.ReadAsync("workspace-pg", opened.TableId, "count", null, null, null, ct, "postgres");
        Assert.True(postgres.Reads > 0);
        Assert.Equal(1, postgres.Aggregates);
        Assert.Equal(0, supabase.Reads);
        Assert.Equal(0, supabase.Describes);
        var reads = postgres.Reads;
        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => windows.ReadAsync("workspace-pg", opened.TableId, null, null, null, null, ct, "supabase"));
        Assert.Equal(reads, postgres.Reads);
        await Assert.ThrowsAsync<SupabaseTableNotFoundException>(() => windows.ReadAsync("other-workspace", opened.TableId, null, null, null, null, ct));
        await Assert.ThrowsAsync<SupabaseTableSourceException>(() => brain.Get<ISupabaseTable>("missing").CreateFromQuery(new("Missing", "select name from customers") { Source = "missing" }));
        Assert.Equal(0, supabase.Describes);
    }

    [Fact]
    public async Task LivePostgresTablePagesFiltersAndAggregates()
    {
        var connection = Environment.GetEnvironmentVariable("DIGITALBRAIN_POSTGRES_TEST_CONNECTION");
        Assert.SkipWhen(string.IsNullOrEmpty(connection), "Set DIGITALBRAIN_POSTGRES_TEST_CONNECTION for live tables.");
        await using var source = Npgsql.NpgsqlDataSource.Create(connection!);
        var tables = LiveTableHosting.CreatePostgresSource(source);
        var ct = TestContext.Current.CancellationToken;
        const string sql = "SELECT i AS id, current_database() AS database FROM generate_series(1,5) i";
        var columns = await tables.DescribeAsync(sql, ct);
        var plan = new QueryPlan(sql, columns, [], null, 2, 2);
        var page = await tables.ExecutePlanAsync(plan, ct);
        Assert.Equal(5, page.Total);
        Assert.Equal(["3", "4"], page.Rows.Select(row => row.Cells[0]));
        var filtered = plan with { Filters = [new("id", "gte", "4")], Offset = 0 };
        Assert.Equal(2, (await tables.ExecutePlanAsync(filtered, ct)).Filtered);
        Assert.Equal("2", (await tables.AggregateAsync(filtered, "count", "", ct)).Value);
        await Assert.ThrowsAsync<ArgumentException>(() => tables.DescribeAsync("DELETE FROM customer_research", ct));
    }

    private sealed class Source(string label) : ILiveTableSource
    {
        public int Reads;
        public int Describes;
        public int Aggregates;
        public Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken)
        { Describes++; return Task.FromResult<IReadOnlyList<SupabaseColumn>>([new("name", "text", "text")]); }
        public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(new QueryPage([new("row-0", [System.Text.Json.JsonSerializer.Serialize(label)])], 1, 1)); }
        public Task<SupabaseTableAggregate> AggregateAsync(QueryPlan plan, string function, string columnId, CancellationToken cancellationToken = default)
        { Aggregates++; return Task.FromResult(new SupabaseTableAggregate(columnId, function, "1")); }
    }
}
