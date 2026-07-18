namespace DigitalBrain.Runtime.Neurons;

/// <summary>
/// Provides ambient context for the current synapse being processed by Orleans.
/// </summary>
public static class NeuronContext
{
    private static readonly AsyncLocal<Synapse?> _current = new();

    public static Synapse? Value
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
