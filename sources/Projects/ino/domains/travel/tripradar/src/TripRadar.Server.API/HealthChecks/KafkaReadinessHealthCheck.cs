using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.API.HealthChecks;

internal sealed class KafkaReadinessHealthCheck(IOptions<Kafka> kafkaOptions) : IHealthCheck
{
    private static readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var bootstrapServers = kafkaOptions.Value.BootstrapServers;
        if (string.IsNullOrWhiteSpace(bootstrapServers))
            return HealthCheckResult.Unhealthy("Kafka bootstrap servers are not configured.");

        if (!EndpointParser.TryParseHostPort(bootstrapServers, 9092, out var host, out var port))
            return HealthCheckResult.Unhealthy("Kafka endpoint could not be parsed from bootstrap servers.");

        try
        {
            using var tcpClient = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectionTimeout);

            await tcpClient.ConnectAsync(host, port, timeout.Token);
            return HealthCheckResult.Healthy("Kafka broker is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Kafka readiness check failed.", ex);
        }
    }
}
