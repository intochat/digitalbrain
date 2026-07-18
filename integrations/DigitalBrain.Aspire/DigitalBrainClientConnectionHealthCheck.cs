using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DigitalBrain;

internal sealed class DigitalBrainClientConnectionHealthCheck :
    IClusterConnectionStatusObserver,
    IHealthCheck
{
    private const int Unknown = 0;
    private const int Connected = 1;
    private const int Disconnected = 2;
    private int _status;
    private int _gatewayCount;

    public void NotifyGatewayCountChanged(
        int currentNumberOfGateways,
        int previousNumberOfGateways,
        bool connectionRecovered)
    {
        Volatile.Write(ref _gatewayCount, currentNumberOfGateways);
        Volatile.Write(
            ref _status,
            currentNumberOfGateways > 0 ? Connected : Disconnected);
    }

    public void NotifyClusterConnectionLost()
    {
        Volatile.Write(ref _gatewayCount, 0);
        Volatile.Write(ref _status, Disconnected);
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var gatewayCount = Volatile.Read(ref _gatewayCount);
        var result = Volatile.Read(ref _status) switch
        {
            Connected => HealthCheckResult.Healthy(
                $"Connected to {gatewayCount} Orleans gateway(s)."),
            Disconnected => HealthCheckResult.Unhealthy(
                "The DigitalBrain Orleans cluster connection is unavailable."),
            Unknown => HealthCheckResult.Degraded(
                "The DigitalBrain Orleans cluster connection has not been observed yet."),
            _ => HealthCheckResult.Unhealthy(
                "The DigitalBrain Orleans cluster connection state is invalid.")
        };
        return Task.FromResult(result);
    }
}
