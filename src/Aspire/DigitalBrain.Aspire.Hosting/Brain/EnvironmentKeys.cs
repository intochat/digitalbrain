namespace DigitalBrain.Aspire.Hosting;

// "DigitalBrain:Integrations:gmail" + "ClientId" -> DigitalBrain__Integrations__gmail__ClientId,
// the same key the module reads back through IConfiguration.
public static class EnvironmentKeys
{
    public static string For(string configurationRoot, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"{configurationRoot}:{name}".Replace(":", "__", StringComparison.Ordinal);
    }
}
