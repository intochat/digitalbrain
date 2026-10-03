using DigitalBrain.Postgres;
using Npgsql;

namespace DigitalBrain.Modules.Postgres.Tests;

public sealed class PostgresProviderFacts
{
    [Fact]
    public async Task LiveProviderReportsTheConnectedDatabaseAndEnforcesReadOnlyQueries()
    {
        var connectionString = Environment.GetEnvironmentVariable("DIGITALBRAIN_POSTGRES_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("Set DIGITALBRAIN_POSTGRES_TEST_CONNECTION to run the PostgreSQL provider test.");
        }
        var ct = TestContext.Current.CancellationToken;
        await using var source = NpgsqlDataSource.Create(PostgresConnectionSettings.Parse(connectionString).ConnectionString);
        await using var databaseCommand = source.CreateCommand("SELECT current_database()");
        var expectedDatabase = Assert.IsType<string>(await databaseCommand.ExecuteScalarAsync(ct));
        var provider = new PostgresProvider(source);

        var connection = await provider.PingAsync(ct);
        Assert.True(connection.Connected);
        Assert.Equal(expectedDatabase, connection.Database);
        Assert.StartsWith("PostgreSQL", connection.ServerVersion, StringComparison.Ordinal);
        Assert.Equal(expectedDatabase, (await provider.ReadSchemaAsync(null, ct)).Database);

        var result = await provider.QueryAsync("SELECT i, true AS flag, NULL::text AS empty FROM generate_series(1, 3) AS i", 2, ct);
        Assert.Equal(2, result.RowCount);
        Assert.True(result.Truncated);
        Assert.Equal(["1", "true", "null"], result.Rows[0]);

        // A SELECT can call a function that writes. The server transaction must reject it,
        // independently of the syntactic guard. The test owns this uniquely named schema.
        var schema = "postgres_test_" + Guid.NewGuid().ToString("N");
        await using var setup = source.CreateCommand($"""
            CREATE SCHEMA {schema};
            CREATE TABLE {schema}.items (id integer);
            CREATE FUNCTION {schema}.write_item() RETURNS integer LANGUAGE plpgsql AS
            'BEGIN INSERT INTO {schema}.items VALUES (1); RETURN 1; END';
            """);
        await setup.ExecuteNonQueryAsync(ct);
        try
        {
            var metadata = await provider.ReadSchemaAsync($"{schema}.items", ct);
            Assert.Equal($"{schema}.items", Assert.Single(metadata.Tables).Name);
            var error = await Assert.ThrowsAsync<PostgresQueryException>(() => provider.QueryAsync($"SELECT {schema}.write_item()", 1, ct));
            Assert.Contains("25006", error.Message, StringComparison.Ordinal);
            await using var count = source.CreateCommand($"SELECT count(*) FROM {schema}.items");
            Assert.Equal(0L, await count.ExecuteScalarAsync(ct));
        }
        finally
        {
            await using var cleanup = source.CreateCommand($"DROP SCHEMA {schema} CASCADE");
            await cleanup.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
