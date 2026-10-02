using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using DigitalBrain.Sdk.Capacity;
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
        services.AddSingleton<IIncomingGrainCallFilter, PostgresLifecycleGuard>();
        services.TryAddSingleton<IPostgresLegacyTables, PostgresLegacyTables>();
        silo.AddStartupTask<PostgresMigrationStartup>();
        services.AddOptions<PostgresModuleOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.PopulateModuleOptions(nameof(PostgresModule), options))
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
        services.TryAddSingleton<IPostgresSourceRegistry, PostgresSourceRegistry>();
        services.AddSingleton<ICapacityConfiguredSource>(provider =>
            new PostgresConfiguredSource(active: provider.GetRequiredService<IConfiguration>()[PostgresCapacityKind.AdminConnectionKey] is null));
        services.AddSingleton<ICapacityProvisioner>(provider =>
            provider.GetRequiredService<IConfiguration>()[PostgresCapacityKind.AdminConnectionKey] is { } admin
                ? new DockerPostgresProvisioner(admin)
                : new InactivePostgresProvisioner());
        services.TryAddSingleton<IPostgresProvider, PostgresProvider>();
        services.TryAddSingleton<IPostgresTableProvider, PostgresTableProvider>();
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
