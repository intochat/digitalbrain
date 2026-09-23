namespace DigitalBrain.Connections;

public enum ConnectionProbeOutcome
{
    Connected = 0,
    Expired = 1,
    Failing = 2,
}

[GenerateSerializer, Alias("connections.probe-result")]
public sealed record ConnectionProbeResult(ConnectionProbeOutcome Outcome, string Detail)
{
    public static ConnectionProbeResult Connected(string detail = "The source answered a read-only probe.") => new(ConnectionProbeOutcome.Connected, detail);

    public static ConnectionProbeResult Expired(string detail) => new(ConnectionProbeOutcome.Expired, detail);

    public static ConnectionProbeResult Failing(string detail) => new(ConnectionProbeOutcome.Failing, detail);
}

// The read-only probe seam. An adapter never mutates the remote source; it validates the credential
// by reading (a version, a schema, a profile) and reports connected, expired or failing.
public interface IConnectionProbe
{
    Task<ConnectionProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("connections.not-configured")]
public sealed class ConnectionNotConfiguredException(string message) : InvalidOperationException(message);