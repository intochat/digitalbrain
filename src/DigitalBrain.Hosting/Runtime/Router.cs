namespace DigitalBrain;

internal sealed class Router(CompositionCatalog catalog)
{
    internal IReadOnlyList<NeuronId> Resolve(NeuronId source, Type synapseType)
        => Resolve(source, synapseType, Dispatch.Broadcast);

    internal IReadOnlyList<NeuronId> Resolve(NeuronId source, Type synapseType, Dispatch dispatch)
    {
        if (!dispatch.IsDirect)
        {
            return [.. catalog.ListenerKindsOf(synapseType)
                .Select(kind => new NeuronId(kind, source.Name))
                .Where(receiver => receiver != source)
                .Distinct()];
        }

        var receiver = dispatch.Receiver!.Value;
        if (SynapseSourceIdentity.Is(receiver))
        {
            throw new DirectDispatchRejectedException(
                $"{receiver} is a source identity, not a behavior receiver.");
        }

        if (!IsKnown(receiver))
        {
            throw new DirectDispatchRejectedException(
                $"neuron kind '{receiver.Kind}' is absent from the catalog");
        }

        if (!Listens(receiver, synapseType))
        {
            throw new DirectDispatchRejectedException(
                $"{receiver} does not handle '{KindOf(synapseType)}'.");
        }

        return [receiver];
    }

    internal bool Listens(NeuronId receiver, Type synapseType)
        => catalog.ListenerKindsOf(synapseType).Contains(receiver.Kind, StringComparer.Ordinal);

    internal bool IsKnown(NeuronId receiver) => catalog.HasNeuronKind(receiver.Kind);

    internal string KindOf(Type synapseType) => catalog.KindOfSynapse(synapseType);

    internal bool AllowsIngress(Type synapseType) => catalog.AllowsIngress(synapseType);
}
