using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace TripRadar.Server.API.HealthChecks;

internal sealed class PostgresReadinessHealthCheck(IConfiguration configuration) : IHealthCheck
{
    private static readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("db") ?? configuration.GetConnectionString("AppDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            return HealthCheckResult.Unhealthy("PostgreSQL connection string is not configured.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectionTimeout);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeout.Token);

            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested is false)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.", ex);
        }
    }
}
