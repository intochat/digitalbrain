using Npgsql;
using Xunit;

namespace DigitalBrain.Apps.CustomerResearcher.Tests.Unit;

public sealed class PostgresResearchFacts
{
    [Fact]
    public async Task LivePostgresUpsertIsWorkspaceScopedAndParameterized()
    {
        var connectionString = Environment.GetEnvironmentVariable("CUSTOMER_RESEARCH_TEST_POSTGRES");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString), "Set CUSTOMER_RESEARCH_TEST_POSTGRES to run the live PostgreSQL persistence test.");
        var ct = TestContext.Current.CancellationToken;
        await using var source = NpgsqlDataSource.Create(connectionString!);
        var repository = new CompanyResearchStore(source);
        var workspace = "research-test-" + Guid.NewGuid().ToString("N");
        var company = new CompanyResearch("O'Reilly; DROP TABLE customers; --", "https://example.com", null, null, null, null, null, []);
        try
        {
            await repository.Save(workspace, "retry", company, ct);
            await repository.Save(workspace, "retry", company with { Summary = "Updated" }, ct);
            await repository.Save(workspace + "-other", "retry", company, ct);
            await using var command = source.CreateCommand("SELECT company_name,summary FROM customer_research WHERE workspace=$1 AND research_id=$2");
            command.Parameters.AddWithValue(workspace);
            command.Parameters.AddWithValue("retry");
            await using var reader = await command.ExecuteReaderAsync(ct);
            Assert.True(await reader.ReadAsync(ct));
            Assert.Equal(company.CompanyName, reader.GetString(0));
            Assert.Equal("Updated", reader.GetString(1));
            Assert.False(await reader.ReadAsync(ct));
        }
        finally
        {
            await using var cleanup = source.CreateCommand("DELETE FROM customer_research WHERE workspace=$1 OR workspace=$2");
            cleanup.Parameters.AddWithValue(workspace);
            cleanup.Parameters.AddWithValue(workspace + "-other");
            await cleanup.ExecuteNonQueryAsync(ct);
        }
    }
}
