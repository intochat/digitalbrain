namespace DigitalBrain.Capabilities.Connections.Integrations;

public interface IIntegrationHandle
{
    string IntegrationId { get; }
    Task<IntegrationSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    Task<string> ReadSecretAsync(string field, CancellationToken cancellationToken = default);
    Task<ReleasedIntegration> ReleaseAsync(CancellationToken cancellationToken = default);
}
