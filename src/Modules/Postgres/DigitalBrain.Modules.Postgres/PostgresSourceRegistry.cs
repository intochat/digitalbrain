using System.Collections.Concurrent;
using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DigitalBrain.Postgres;

internal static class PostgresCapacityKind
{
    public const string Kind = "postgres";
    public const string PlatformOrigin = "platform";
    public const string DatabasePrefix = "db:";
    public const string AdminConnectionKey = "DigitalBrain:Capacity:Postgres:AdminConnection";

    // A table pinned before origins existed (Origin == null) lives in the platform source.
    public static string OriginOrPlatform(string? origin) => string.IsNullOrEmpty(origin) ? PlatformOrigin : origin;
}

// Inactive when an admin connection is present: local hosted runs provision per brain instead of
// sharing the platform database, while ConnectionStrings:postgres keeps serving startup and health.
internal sealed class PostgresConfiguredSource(bool active) : ICapacityConfiguredSource
{
    public string Kind => active ? PostgresCapacityKind.Kind : PostgresCapacityKind.Kind + ":inactive";
    public string Origin => PostgresCapacityKind.PlatformOrigin;
}

internal interface IPostgresSourceRegistry
{
    NpgsqlDataSource Get(string origin);
}

internal sealed class PostgresSourceRegistry(IServiceProvider provider, IConfiguration configuration) : IPostgresSourceRegistry, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _provisioned = new(StringComparer.Ordinal);

    public NpgsqlDataSource Get(string origin)
    {
        if (origin == PostgresCapacityKind.PlatformOrigin)
        { return provider.GetRequiredKeyedService<NpgsqlDataSource>(PostgresHosting.DataSourceKey); }
        if (!origin.StartsWith(PostgresCapacityKind.DatabasePrefix, StringComparison.Ordinal))
        { throw new PostgresUnavailableException("Unknown Postgres capacity origin."); }
        return _provisioned.GetOrAdd(origin, key =>
        {
            var admin = configuration[PostgresCapacityKind.AdminConnectionKey]
                ?? throw new PostgresUnavailableException("This table's database is not reachable from this deployment.");
            var builder = new NpgsqlConnectionStringBuilder(PostgresConnectionSettings.Parse(admin).ConnectionString)
            { Database = key[PostgresCapacityKind.DatabasePrefix.Length..] };
            return NpgsqlDataSource.Create(builder.ConnectionString);
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _provisioned.Values) { await source.DisposeAsync(); }
    }
}
