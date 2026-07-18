namespace DigitalBrain.Runtime.Streams;

public enum StreamProviderMode
{
    Memory,
    Redis,
}

public static class StreamProviderConfig
{
    public const string ConfigKey = "DigitalBrain:Streams:Mode";
    public const string DefaultMode = "memory";
    public const string SynapseProviderName = "synapse";
    public const string PubSubStoreName = "PubSubStore";

    public static StreamProviderMode ResolveMode(IConfiguration configuration)
    {
        var raw = configuration[ConfigKey] ?? DefaultMode;
        return raw.ToLowerInvariant() switch
        {
            "memory" => StreamProviderMode.Memory,
            "redis" => StreamProviderMode.Redis,
            _ => throw new InvalidOperationException(
                $"Unknown {ConfigKey} '{raw}'. Expected 'memory' or 'redis'."),
        };
    }
}
