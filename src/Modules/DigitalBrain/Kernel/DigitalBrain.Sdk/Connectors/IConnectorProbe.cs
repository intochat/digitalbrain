namespace DigitalBrain.Sdk.Connectors;

public enum ConnectorProbeOutcome
{
    Connected = 0,
    Expired = 1,
    Failing = 2,
}

[GenerateSerializer, Alias("connections.probe-result")]
public sealed record ConnectorProbeResult(ConnectorProbeOutcome Outcome, string Detail)
{
    public static ConnectorProbeResult Connected(string detail = "The connector probe succeeded.") => new(ConnectorProbeOutcome.Connected, detail);

    public static ConnectorProbeResult Expired(string detail) => new(ConnectorProbeOutcome.Expired, detail);

    public static ConnectorProbeResult Failing(string detail) => new(ConnectorProbeOutcome.Failing, detail);
}

// Integrations can replace the default presence check with a source-specific probe.
public interface IConnectorProbe
{
    Task<ConnectorProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("connections.not-configured")]
public sealed class ConnectorNotConfiguredException(string message) : InvalidOperationException(message);
