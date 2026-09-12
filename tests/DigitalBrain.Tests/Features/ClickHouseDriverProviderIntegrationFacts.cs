using System.Text.Json;
using ClickHouse.Driver;
using DigitalBrain.ClickHouse;
using DigitalBrain.UI;
using Testcontainers.ClickHouse;
using Xunit;

namespace DigitalBrain.Tests;

// Runs the real driver provider against a throwaway ClickHouse container seeded with leads.sql.
// Docker is required, so the whole class is skipped unless DIGITALBRAIN_CLICKHOUSE_TESTS=1.
public sealed class ClickHouseDriverProviderIntegrationFacts(ClickHouseServerFixture server) : IClassFixture<ClickHouseServerFixture>
{
    private const string Skip = "Set DIGITALBRAIN_CLICKHOUSE_TESTS=1 (Docker required) to run the ClickHouse integration tests.";

    public static bool DockerTestsEnabled => ClickHouseServerFixture.Enabled;

    [Fact(Skip = Skip, SkipUnless = nameof(DockerTestsEnabled))]
    public async Task Schema_lists_only_the_seeded_database()
    {
        var schema = await server.Provider.ReadSchemaAsync(null, TestContext.Current.CancellationToken);
        Assert.Equal("digitalbrain", schema.Database);
        Assert.Equal(["companies_current", "employees", "facts", "sources"], schema.Tables.Select(table => table.Name));
        var companies = schema.Tables.Single(table => table.Name == "companies_current");
        Assert.Equal("ReplacingMergeTree", companies.Engine);
        Assert.Equal("number", companies.Columns.Single(column => column.Name == "employee_count").TableType);
        Assert.Equal("date", companies.Columns.Single(column => column.Name == "founded_on").TableType);
        Assert.Equal("text", companies.Columns.Single(column => column.Name == "activity_tags").TableType);
        var only = await server.Provider.ReadSchemaAsync("employees", TestContext.Current.CancellationToken);
        Assert.Equal("employees", Assert.Single(only.Tables).Name);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(DockerTestsEnabled))]
    public async Task Queries_are_capped_typed_and_flag_truncation()
    {
        var capped = await server.Provider.QueryAsync("SELECT name, employee_count, founded_on, is_active, activity_tags FROM companies_current ORDER BY name", 5, TestContext.Current.CancellationToken);
        Assert.True(capped.Truncated);
        Assert.Equal(5, capped.RowCount);
        Assert.Equal(["text", "number", "date", "boolean", "text"], capped.Columns.Select(column => column.TableType));
        Assert.Equal(JsonValueKind.Number, capped.Rows[0][1].ValueKind);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", capped.Rows[0][2].GetString());
        Assert.StartsWith("[", capped.Rows[0][4].GetString(), StringComparison.Ordinal);

        var all = await server.Provider.QueryAsync("SELECT count() AS companies FROM companies_current", 200, TestContext.Current.CancellationToken);
        Assert.False(all.Truncated);
        Assert.Equal(30, all.Rows[0][0].GetInt64());
    }

    [Fact(Skip = Skip, SkipUnless = nameof(DockerTestsEnabled))]
    public async Task Plans_page_filter_and_sort_on_the_server()
    {
        var columns = await server.Provider.DescribeAsync("SELECT name, country, employee_count, activity_tags FROM companies_current", TestContext.Current.CancellationToken);
        Assert.Equal(["name", "country", "employee_count", "activity_tags"], columns.Select(column => column.Name));

        var page = await server.Provider.ExecutePlanAsync(new QueryPlan("SELECT name, country, employee_count, activity_tags FROM companies_current",
            columns, [], new TableSort("name"), 10, 10), TestContext.Current.CancellationToken);
        Assert.Equal(10, page.Rows.Count);
        Assert.Equal(30, page.Total);
        Assert.Equal(30, page.Filtered);
        Assert.Equal("row-10", page.Rows[0].Id);

        var roofing = await server.Provider.ExecutePlanAsync(new QueryPlan("SELECT name, country, employee_count, activity_tags FROM companies_current",
            columns,
            [new TableFilter("name", "contains", JsonSerializer.SerializeToElement("ROOF")), new TableFilter("employee_count", "gte", JsonSerializer.SerializeToElement(50))],
            new TableSort("employee_count", Descending: true), 0, 50), TestContext.Current.CancellationToken);
        Assert.Equal(30, roofing.Total);
        Assert.Equal(1, roofing.Filtered);
        Assert.Equal("Thames Roofing Ltd", Assert.Single(roofing.Rows).Cells[0].GetString());

        var nulls = await server.Provider.ExecutePlanAsync(new QueryPlan("SELECT name, country, employee_count, activity_tags FROM companies_current",
            columns, [new TableFilter("employee_count", "isNull")], null, 0, 50), TestContext.Current.CancellationToken);
        Assert.Equal("Cotswold Stone Masons", Assert.Single(nulls.Rows).Cells[0].GetString());
    }

    [Fact(Skip = Skip, SkipUnless = nameof(DockerTestsEnabled))]
    public async Task Writes_are_refused_by_the_guard_and_by_the_server()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => server.Provider.QueryAsync("INSERT INTO companies_current (company_id) VALUES ('x')", 10, TestContext.Current.CancellationToken));
        // The same readonly=2 request settings the provider uses stop a write that bypasses the guard.
        var options = new QueryOptions { CustomSettings = new Dictionary<string, object> { ["readonly"] = 2 } };
        await Assert.ThrowsAsync<ClickHouseServerException>(() => server.Client.ExecuteNonQueryAsync("INSERT INTO digitalbrain.sources (source_id, url, kind, fetched_at) VALUES ('x', 'x', 'x', now())", null, options, TestContext.Current.CancellationToken));
        var failure = await Assert.ThrowsAsync<ClickHouseQueryException>(() => server.Provider.QueryAsync("SELECT missing_column FROM companies_current", 10, TestContext.Current.CancellationToken));
        Assert.Contains("missing_column", failure.Message, StringComparison.Ordinal);
    }
}

public sealed class ClickHouseServerFixture : IAsyncLifetime
{
    // Keep in step with the tag Aspire.Hosting.ClickHouse 13.5.3 pins; see docs/clickhouse/NOTES.md.
    private const string Image = "clickhouse/clickhouse-server:25.8";

    private ClickHouseContainer? _container;
    private ClickHouseClient? _client;

    public static bool Enabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_CLICKHOUSE_TESTS") == "1";

    public ClickHouseClient Client => _client ?? throw new InvalidOperationException(nameof(Enabled));

    internal ClickHouseDriverProvider Provider { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        _container = new ClickHouseBuilder(Image).Build();
        await _container.StartAsync();
        _client = new ClickHouseClient(_container.GetConnectionString());
        foreach (var statement in SeedStatements())
        {
            await _client.ExecuteNonQueryAsync(statement);
        }

        Provider = new ClickHouseDriverProvider(new ClickHouseClient(_container.GetConnectionString() + ";Database=digitalbrain"), "digitalbrain");
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    // The seed is one file of ;-terminated statements without ; inside literals; the SET line is
    // per-session in clickhouse-client and per-request over HTTP, so it is folded into the client.
    private static IEnumerable<string> SeedStatements()
    {
        var directory = AppContext.BaseDirectory;
        string? seed = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "src", "Modules", "ClickHouse", "Aspire.Hosting", "Seeds", "leads.sql");
            if (File.Exists(candidate))
            {
                seed = candidate;
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }

        var sql = File.ReadAllText(seed ?? throw new FileNotFoundException("Seeds/leads.sql was not found above the test directory."));
        return sql.Split(';')
            .Select(statement => string.Join('\n', statement.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal))).Trim())
            .Where(statement => statement.Length > 0 && !statement.StartsWith("SET ", StringComparison.OrdinalIgnoreCase));
    }
}
