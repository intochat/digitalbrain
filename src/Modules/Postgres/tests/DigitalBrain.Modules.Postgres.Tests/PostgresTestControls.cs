using DigitalBrain.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Modules.Postgres.Tests;

internal sealed class PostgresTestSiloBuilder(IServiceCollection services, IConfiguration configuration) : ISiloBuilder
{
    public IServiceCollection Services => services;
    public IConfiguration Configuration => configuration;
}

internal sealed class PostgresTestControls : IPostgresProvider
{
    public Task<PostgresQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
    {
        Assert.Equal("select id from people", sql);
        Assert.Equal(10, maxRows);
        return Task.FromResult(new PostgresQueryResult([new("id", "int4", "number")], [["7"]], 1, false, 0));
    }

    public Task<PostgresSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
    {
        Assert.Equal("reporting.people", table);
        return Task.FromResult(new PostgresSchema("sample", [new("reporting.people", "BASE TABLE", null, [new("id", "int4", "number")])]));
    }

    public Task<PostgresConnection> PingAsync(CancellationToken cancellationToken)
        => Task.FromResult(new PostgresConnection(true, "sample", "PostgreSQL", "Npgsql"));
}
