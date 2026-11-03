using DigitalBrain.Modules.Configuration;

namespace DigitalBrain.Capabilities.Connections.Connectors;

// The Integration Neuron reads its configured fields from host configuration under
// "<Root>:Integrations:<id>:<field>". Every integration uses the same key layout.
public static class IntegrationSchema
{
    public static string SectionName => ModuleConfiguration.IntegrationsSectionName;

    public static string Key(string id, string field) => ConfigurationKeyGrammar.IntegrationKey(id, field);
}
