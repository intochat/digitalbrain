namespace DigitalBrain.Modules.Configuration;

// The dependency-free half of the configuration grammar: a ':'-separated key path, its '__' environment
// form, and an integration field's key.
public static class ConfigurationKeyGrammar
{
    public static string EnvironmentName(string configurationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        return configurationKey.Replace(":", "__", StringComparison.Ordinal);
    }

    public static string IntegrationKey(string id, string field) => $"{ModuleConfiguration.IntegrationsSectionName}:{id}:{field}";
}
