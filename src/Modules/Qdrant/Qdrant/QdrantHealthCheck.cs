using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DigitalBrain.Qdrant;

internal sealed class QdrantHealthCheck(IQdrantProvider provider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = await provider.PingAsync(cancellationToken).ConfigureAwait(false);
        return connection.Connected
            ? HealthCheckResult.Healthy($"Qdrant serves collection '{connection.Collection}'.")
            : HealthCheckResult.Unhealthy($"Qdrant collection '{connection.Collection}' is unreachable.");
    }
}
