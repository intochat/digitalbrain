using DigitalBrain.Contracts.Data;
using DigitalBrain.Postgres;
using DigitalBrain.Testing.Module;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Postgres.Tests;

public sealed class PostgresRowSourceFacts
{
    [Fact]
    public async Task ReadCompilesRowQueryIntoGuardSafeSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new RecordingPostgresProvider();
        await using var brain = await ModuleTest.Create().ConfigureSilo(silo =>
            silo.Services.AddSingleton<IPostgresProvider>(provider)).StartAsync(ct);
        var source = brain.Get<IPostgresRows>("public.people");

        var schema = await source.ReadSchema();
        var page = await source.Read(new RowQuery { Columns = ["name"], Filters = [new("name", "eq", "ada")] });

        Assert.Equal("name", Assert.Single(schema.Columns).Name);
        Assert.Equal("ada", Assert.Single(Assert.Single(page.Rows).Values));
        var sql = Assert.Single(provider.Sql);
        Assert.Equal(sql, PostgresQueryGuard.Normalize(sql));
        Assert.Contains("""FROM "public"."people" WHERE "name" = 'ada'""", sql, StringComparison.Ordinal);
    }

    private sealed class RecordingPostgresProvider : IPostgresProvider
    {
        public List<string> Sql { get; } = [];

        public Task<PostgresQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
        {
            Sql.Add(sql);
            return Task.FromResult(new PostgresQueryResult([new("name", "text", "text")], [["\"ada\""]], 1, false, 0));
        }

        public Task<PostgresSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
            => Task.FromResult(new PostgresSchema("db", [new(table!, "BASE TABLE", 1, [new("name", "text", "text")])]));

        public Task<PostgresConnection> PingAsync(CancellationToken cancellationToken)
            => Task.FromResult(new PostgresConnection(true, "db", "PostgreSQL", "Npgsql"));
    }
}
