namespace DigitalBrain.Capabilities.Connections.Integrations;

// Platform runs these after Integration seeding and before its startup task completes.
public interface IIntegrationStartup
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
