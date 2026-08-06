namespace DigitalBrain;

internal sealed class Router(CompositionCatalog catalog)
{
    internal IReadOnlyList<NeuronId> Resolve(NeuronId source, Type synapseType)
        => [.. catalog.ListenerKindsOf(synapseType)
            .Select(kind => new NeuronId(kind, source.Name))
            .Where(receiver => receiver != source)
            .Distinct()];

    internal bool Listens(NeuronId receiver, Type synapseType)
        => catalog.ListenerKindsOf(synapseType).Contains(receiver.Kind, StringComparer.Ordinal);

    internal bool IsKnown(NeuronId receiver) => catalog.HasNeuronKind(receiver.Kind);

    internal string KindOf(Type synapseType) => catalog.KindOfSynapse(synapseType);
}
