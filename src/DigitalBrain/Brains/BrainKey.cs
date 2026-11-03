namespace DigitalBrain.Brains;

// Every brain-owned neuron key is "{brain}" or starts with "{brain}/".
public static class BrainKey
{
    public static string BrainOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var separator = key.IndexOf('/', StringComparison.Ordinal);
        return separator < 0 ? key : key[..separator];
    }

    public static bool BelongsTo(string key, string brain)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(brain);
        return key.Length == brain.Length
            ? string.Equals(key, brain, StringComparison.Ordinal)
            : key.Length > brain.Length && key[brain.Length] == '/' && key.StartsWith(brain, StringComparison.Ordinal);
    }

    // The brain a key belongs to for access checks: the first '/'-segment, when it is a brain id.
    // A key with no '/' names nothing brain-scoped.
    public static string? ScopeOf(string key) => NeuronAddress.ScopeOf(key);
}
