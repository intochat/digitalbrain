using DigitalBrain.Spi;

namespace DigitalBrain.Spi
{
    /// <summary>Finds the gate neurons a signal must pass before it is published.</summary>
    public interface IGateIndex
    {
        /// <summary>The gates a signal of type <paramref name="signal"/> reaches in <paramref name="brain"/>, in the order they run.</summary>
        /// <param name="brain">The publishing brain.</param>
        /// <param name="signal">The signal type.</param>
        /// <returns>Each gate neuron with the synapse it runs.</returns>
        IReadOnlyList<(NeuronId Gate, SynapseDescriptor Synapse)> GatesFor(BrainId brain, Type signal);
    }
}

namespace DigitalBrain
{
    /// <summary>An <see cref="IGateIndex"/> over a <see cref="SynapseCatalog"/>: the gate neuron is <c>{brain}/{StateAlias}/{Key}</c>.</summary>
    /// <param name="catalog">The synapse catalog.</param>
    public sealed class CatalogGateIndex(SynapseCatalog catalog) : IGateIndex
    {
        /// <inheritdoc />
        public IReadOnlyList<(NeuronId Gate, SynapseDescriptor Synapse)> GatesFor(BrainId brain, Type signal)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            return [.. catalog.GatesOf(signal).Select(d => (new NeuronId(brain, d.StateAlias, d.Key), d))];
        }
    }
}
