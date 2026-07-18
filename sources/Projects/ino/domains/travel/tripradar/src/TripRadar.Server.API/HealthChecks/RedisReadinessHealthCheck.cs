using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TripRadar.Server.API.HealthChecks;

internal sealed class RedisReadinessHealthCheck(IConfiguration configuration, IHostEnvironment hostEnvironment) : IHealthCheck
{
    private static readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("redis");
        var isDev = hostEnvironment.IsDevelopment();

        if (string.IsNullOrWhiteSpace(connectionString))
            return isDev ? HealthCheckResult.Healthy("Redis connection string is absent, in-memory cache fallback is active.") : HealthCheckResult.Unhealthy("Redis connection string is not configured.");

        if (!EndpointParser.TryParseHostPort(connectionString, 6379, out var host, out var port))
            return HealthCheckResult.Unhealthy("Redis endpoint could not be parsed from connection string.");

        try
        {
            using var tcpClient = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectionTimeout);

            await tcpClient.ConnectAsync(host, port, timeout.Token);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis readiness check failed.", ex);
        }
    }
}
