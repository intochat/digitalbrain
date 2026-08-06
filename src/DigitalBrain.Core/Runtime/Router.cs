namespace DigitalBrain;

internal sealed class Router(ICatalog catalog)
{
    internal IReadOnlyList<NeuronId> Resolve(NeuronId source, Type factType)
        => [.. catalog.ListenerKindsOf(factType)
            .Select(kind => new NeuronId(kind, source.Name))
            .Where(receiver => receiver != source)
            .Distinct()];

    internal bool Listens(NeuronId receiver, Type factType)
        => catalog.ListenerKindsOf(factType).Contains(receiver.Kind, StringComparer.Ordinal);

    internal bool IsKnown(NeuronId receiver) => catalog.HasNeuronKind(receiver.Kind);

    internal string KindOf(Type factType) => catalog.KindOfFact(factType);
}
