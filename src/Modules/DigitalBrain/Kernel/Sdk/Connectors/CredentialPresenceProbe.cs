namespace DigitalBrain.Sdk.Connectors;

internal sealed class CredentialPresenceProbe : IConnectorProbe
{
    public Task<ConnectorProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default)
        => Task.FromResult(string.IsNullOrWhiteSpace(credential)
            ? ConnectorProbeResult.Failing("No credential is stored for this connector.")
            : ConnectorProbeResult.Connected("A credential is stored; the source has not been verified."));
}
