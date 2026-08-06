namespace DigitalBrain;

internal static class SynapseSourceIdentity
{
    internal const string Kind = "digitalbrain.synapse-source";

    internal static NeuronId For(SynapseSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Name);
        return new NeuronId(Kind, source.Name);
    }

    internal static bool Is(NeuronId id)
        => string.Equals(id.Kind, Kind, StringComparison.Ordinal);
}
