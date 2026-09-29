using DigitalBrain.AI.Agents;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Orleans.Hosting;

namespace DigitalBrain.Postgres;

public static class PostgresHosting
{
    public const string DataSourceKey = "DigitalBrain.Postgres";

    public static ISiloBuilder AddPostgres(this ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        if (services.Any(descriptor => descriptor.ServiceType == typeof(PostgresRegistration))) { return silo; }
        services.AddSingleton<PostgresRegistration>();
        services.AddOptions<PostgresModuleOptions>()
            .Bind(silo.Configuration.GetSection(PostgresModuleOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionName), "Postgres requires a connection name.")
            .Validate<IConfiguration>((options, configuration) => IsConnectionValid(configuration.GetConnectionString(options.ConnectionName)),
                "Postgres requires a valid PostgreSQL URI or Npgsql connection string for its configured connection name.")
            .ValidateOnStart();
        services.TryAddKeyedSingleton<NpgsqlDataSource>(DataSourceKey, (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<PostgresModuleOptions>>().Value;
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString(options.ConnectionName)!;
            return NpgsqlDataSource.Create(PostgresConnectionSettings.Parse(connection).ConnectionString);
        });
        services.TryAddSingleton<IPostgresProvider, PostgresProvider>();
        services.AddLiveTables();
        services.TryAddKeyedSingleton<ILiveTableSource>("postgres", (provider, _) =>
            LiveTableHosting.CreatePostgresSource(provider.GetRequiredKeyedService<NpgsqlDataSource>(DataSourceKey)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolFactory, PostgresTools>());
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
        return silo;
    }

    private static bool IsConnectionValid(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) { return false; }
        try { _ = PostgresConnectionSettings.Parse(connection); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private sealed class PostgresRegistration;

    private sealed class PostgresHealthCheck(IPostgresProvider provider) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var connection = await provider.PingAsync(cancellationToken).ConfigureAwait(false);
            return connection.Connected
                ? HealthCheckResult.Healthy("Postgres is reachable.")
                : HealthCheckResult.Unhealthy("Postgres is unreachable.");
        }
    }
}
