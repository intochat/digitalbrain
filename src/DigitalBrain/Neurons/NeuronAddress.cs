namespace DigitalBrain;

// The runtime SPI: the address and resolver a runtime implements.

// Key is the full neuron key, including any "{brain}/" prefix. Brain is derived from it and is null for
// brain-less neurons (platform, cluster, ledger). StorageKey is "{NeuronType}/{Key}".
public readonly record struct NeuronAddress(string NeuronType, string Key)
{
    public BrainId? Brain => Key is not null && ScopeOf(Key) is { } brain ? new BrainId(brain) : null;

    public string StorageKey => $"{NeuronType}/{Key}";

    public override string ToString() => StorageKey;

    // The one brain-id rule, DigitalBrain.BrainId.IsValid:
    // 1-200 characters, no control characters, no path separators and no ':'; a leading '@' is reserved for keys outside every brain.
    public static bool IsBrainId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value)
        => BrainId.IsValid(value);

    // The brain a key belongs to: its first '/'-segment when that is a brain id. A key with no '/' names no brain.
    public static string? ScopeOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var separator = key.IndexOf('/', StringComparison.Ordinal);
        return separator > 0 && IsBrainId(key[..separator]) ? key[..separator] : null;
    }
}

// Implemented by each runtime: the only place an id becomes a reference.
public interface INeuronResolver
{
    // Composes "{brain}/{relative}". relative is the key under the brain, not a DigitalBrain neuron id.
    T Resolve<T>(BrainId brain, string relative) where T : class;

    // Brain-less neurons and the transitional raw-key path; Framework and Authority only.
    T ResolveKey<T>(string fullKey) where T : class;
}
