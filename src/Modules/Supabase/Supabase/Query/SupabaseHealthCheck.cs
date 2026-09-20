using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DigitalBrain.Supabase;

internal sealed class SupabaseHealthCheck(ISupabaseProvider provider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = await provider.PingAsync(cancellationToken).ConfigureAwait(false);
        return connection.Connected
            ? HealthCheckResult.Healthy($"Supabase {connection.ServerVersion} serves database '{connection.Database}'.")
            : HealthCheckResult.Unhealthy($"Supabase database '{connection.Database}' is unreachable.");
    }
}