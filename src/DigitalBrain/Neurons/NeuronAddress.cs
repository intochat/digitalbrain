namespace DigitalBrain.Brains;

public readonly record struct NeuronAddress(string NeuronType, string Key)
{
    public BrainId? Brain => Key is not null && ScopeOf(Key) is { } brain ? new BrainId(brain) : null;

    public string StorageKey => $"{NeuronType}/{Key}";

    public override string ToString() => StorageKey;

    public static bool IsBrainId([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value)
        => BrainId.IsValid(value);

    public static string? ScopeOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (NeuronId.TryParse(key, out var neuronId))
        {
            return neuronId.Brain.Value;
        }

        var separator = key.IndexOf('/', StringComparison.Ordinal);
        return separator > 0 && BrainId.IsValid(key[..separator]) ? key[..separator] : null;
    }
}
