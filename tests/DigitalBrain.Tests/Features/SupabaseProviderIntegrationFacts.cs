using System.Text.Json;
using DigitalBrain.Supabase;
using DigitalBrain.UI;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SupabaseProviderIntegrationFacts(SupabaseServerFixture server) : IClassFixture<SupabaseServerFixture>
{
    private const string Skip = "Set DIGITALBRAIN_SUPABASE_TESTS=1 (Docker required).";
    public static bool Enabled => SupabaseServerFixture.Enabled;

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public async Task Displayed_text_values_round_trip_through_equality_filters()
    {
        const string sql = "SELECT '2026-01-01T12:00:00Z'::timestamptz AS moment, ARRAY['a','b'] AS tags, decode('abcd', 'hex') AS bytes";
        var result = await server.Provider.QueryAsync(sql, 10, TestContext.Current.CancellationToken);
        for (var index = 0; index < result.Columns.Count; index++)
        {
            var page = await server.Provider.ExecutePlanAsync(new QueryPlan(sql, result.Columns,
                [new TableFilter(result.Columns[index].Name, "eq", result.Rows[0][index])], null, 0, 10), TestContext.Current.CancellationToken);
            Assert.Single(page.Rows);
        }
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public async Task Arbitrary_precision_and_nonfinite_numbers_are_preserved_as_text()
    {
        var result = await server.Provider.QueryAsync("SELECT 1e40::numeric AS huge, 'NaN'::numeric AS nan, 1.234567890123456789::numeric AS precise", 10, TestContext.Current.CancellationToken);
        Assert.Equal("10000000000000000000000000000000000000000", result.Rows[0][0].GetString());
        Assert.Equal("NaN", result.Rows[0][1].GetString());
        Assert.Equal("1.234567890123456789", result.Rows[0][2].GetString());
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public async Task Queries_are_typed_capped_and_run_in_read_only_transactions()
    {
        var result = await server.Provider.QueryAsync("SELECT id, name, active, born, metadata FROM public.people ORDER BY id", 2, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.RowCount);
        Assert.True(result.Truncated);
        Assert.Equal(["number", "text", "boolean", "date", "text"], result.Columns.Select(c => c.TableType));
        Assert.Equal(1, result.Rows[0][0].GetInt32());
        Assert.True(result.Rows[0][2].GetBoolean());
        Assert.Equal("2020-01-02", result.Rows[0][3].GetString());
        var mode = await server.Provider.QueryAsync("SELECT current_setting('transaction_read_only') AS mode", 10, TestContext.Current.CancellationToken);
        Assert.Equal("on", mode.Rows[0][0].GetString());
        // A SELECT that invokes a user function can evade token checks, but must still be refused by PostgreSQL.
        await Assert.ThrowsAsync<SupabaseQueryException>(() => server.Provider.QueryAsync("SELECT public.try_write()", 10, TestContext.Current.CancellationToken));
        var count = await server.Provider.QueryAsync("SELECT count(*) FROM public.people", 10, TestContext.Current.CancellationToken);
        Assert.Equal(3, count.Rows[0][0].GetInt64());
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public async Task Schema_and_health_report_application_database_metadata()
    {
        var schema = await server.Provider.ReadSchemaAsync(null, TestContext.Current.CancellationToken);
        Assert.Equal(["demo.companies", "demo.people", "public.people"], schema.Tables.Select(t => t.Name));
        var table = Assert.Single(schema.Tables, t => t.Name == "public.people");
        Assert.Equal("public.people", table.Name);
        Assert.Contains(table.Columns, column => column.Name == "born" && column.TableType == "date");
        Assert.Single((await server.Provider.ReadSchemaAsync("public.people", TestContext.Current.CancellationToken)).Tables);
        Assert.Empty((await server.Provider.ReadSchemaAsync("auth.users", TestContext.Current.CancellationToken)).Tables);
        Assert.True((await server.Provider.PingAsync(TestContext.Current.CancellationToken)).Connected);
    }

    [Theory(Skip = Skip, SkipUnless = nameof(Enabled))]
    [InlineData("demo.companies")]
    [InlineData("postgres.demo.companies")]
    [InlineData("\"demo\".\"companies\"")]
    public async Task Custom_schema_names_discover_queryable_tables(string name)
    {
        var schema = await server.Provider.ReadSchemaAsync(name, TestContext.Current.CancellationToken);
        var table = Assert.Single(schema.Tables);
        Assert.Equal("demo.companies", table.Name);
        Assert.Equal(["id", "company_name"], table.Columns.Select(c => c.Name));
        var result = await server.Provider.QueryAsync($"SELECT company_name FROM {table.Name}", 10, TestContext.Current.CancellationToken);
        Assert.Equal("Demo company", Assert.Single(result.Rows)[0].GetString());
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public async Task Bare_names_keep_same_named_tables_separate_and_wrong_database_is_not_resolved()
    {
        var schema = await server.Provider.ReadSchemaAsync("people", TestContext.Current.CancellationToken);
        Assert.Equal(["demo.people", "public.people"], schema.Tables.Select(t => t.Name));
        Assert.Single(schema.Tables[0].Columns);
        Assert.Equal(5, schema.Tables[1].Columns.Count);
        Assert.Empty((await server.Provider.ReadSchemaAsync("wrong.demo.companies", TestContext.Current.CancellationToken)).Tables);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public async Task Live_tables_page_filter_sort_and_parameterize_values()
    {
        var sql = "SELECT id, name FROM public.people";
        var columns = await server.Provider.DescribeAsync(sql, TestContext.Current.CancellationToken);
        var page = await server.Provider.ExecutePlanAsync(new QueryPlan(sql, columns,
            [new TableFilter("name", "contains", JsonSerializer.SerializeToElement("AL"))],
            new TableSort("id", Descending: true), 0, 1), TestContext.Current.CancellationToken);
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Filtered);
        Assert.Equal(3, Assert.Single(page.Rows).Cells[0].GetInt32());
        var injection = await server.Provider.ExecutePlanAsync(new QueryPlan(sql, columns,
            [new TableFilter("name", "eq", JsonSerializer.SerializeToElement("' OR true --"))], null, 0, 10), TestContext.Current.CancellationToken);
        Assert.Empty(injection.Rows);
        Assert.Equal(0, injection.Filtered);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(Enabled))]
    public async Task Cancellation_is_preserved_and_query_errors_do_not_expose_connection_secrets()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server.Provider.QueryAsync("SELECT 1", 10, cancelled.Token));
        var error = await Assert.ThrowsAsync<SupabaseQueryException>(() => server.Provider.QueryAsync("SELECT missing FROM public.people", 10, TestContext.Current.CancellationToken));
        Assert.Contains("42703", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SupabaseServerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _source;
    public static bool Enabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_SUPABASE_TESTS") == "1";
    internal SupabaseProvider Provider { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (!Enabled) { return; }
        _container = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("postgres").Build();
        await _container.StartAsync();
        _source = NpgsqlDataSource.Create(_container.GetConnectionString());
        await using var seed = _source.CreateCommand("""
            CREATE TABLE public.people (id integer, name text, active boolean, born date, metadata jsonb);
            INSERT INTO public.people VALUES
                (1, 'Alice', true, '2020-01-02', '{"a":1}'),
                (2, 'Bob', false, NULL, NULL),
                (3, 'Alfred', true, '2021-01-01', '{}');
            CREATE SCHEMA auth;
            CREATE TABLE auth.users (id integer);
            CREATE SCHEMA demo;
            CREATE TABLE demo.companies (id integer, company_name text);
            INSERT INTO demo.companies VALUES (1, 'Demo company');
            CREATE TABLE demo.people (custom_id integer);
            CREATE FUNCTION public.try_write() RETURNS integer LANGUAGE plpgsql AS $$
            BEGIN INSERT INTO public.people (id) VALUES (4); RETURN 4; END $$;
            """);
        await seed.ExecuteNonQueryAsync();
        Provider = new SupabaseProvider(_source);
    }

    public async ValueTask DisposeAsync()
    {
        if (_source is not null) { await _source.DisposeAsync(); }
        if (_container is not null) { await _container.DisposeAsync(); }
    }
}
