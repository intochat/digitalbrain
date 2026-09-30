using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DigitalBrain.ClickHouse;

internal sealed class ClickHouseHealthCheck(IClickHouseProvider provider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = await provider.PingAsync(cancellationToken).ConfigureAwait(false);
        return connection.Connected
            ? HealthCheckResult.Healthy($"ClickHouse {connection.ServerVersion} serves database '{connection.Database}'.")
            : HealthCheckResult.Unhealthy($"ClickHouse database '{connection.Database}' is unreachable.");
    }
}
