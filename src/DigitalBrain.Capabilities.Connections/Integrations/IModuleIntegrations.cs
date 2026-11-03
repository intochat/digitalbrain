namespace DigitalBrain.Capabilities.Connections.Integrations;

public interface IModuleIntegrations<TModule> where TModule : class
{
    IIntegrationHandle Get(string integrationId);
}
