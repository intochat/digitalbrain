namespace DigitalBrain;

internal sealed class ProducedSynapseStager(Journal journal, Router router, ISynapseSerialization serialization)
{
    internal void ValidateAuthored(Synapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        if (synapse is DeliveryFailed)
        {
            throw new InvalidOperationException(
                $"{nameof(DeliveryFailed)} is recorded only by Hosting after delivery exhaustion.");
        }
    }

    internal void ValidateIngress(Synapse synapse)
    {
        ValidateAuthored(synapse);
        if (!router.AllowsIngress(synapse.GetType()))
        {
            throw new InvalidOperationException(
                $"{router.KindOf(synapse.GetType())} is not registered as external ingress.");
        }
    }

    internal void ValidateForRecording(NeuronId source, Synapse synapse, Dispatch dispatch)
    {
        ValidateAuthored(synapse);
        if (router.AllowsIngress(synapse.GetType()))
        {
            throw new AuthoredSynapseRejectedException(
                $"{router.KindOf(synapse.GetType())} is reserved for external ingress and cannot be emitted by {source}.");
        }

        _ = router.KindOf(synapse.GetType());
        _ = router.Resolve(source, synapse.GetType(), dispatch);
    }

    internal long StageAuthored(
        NeuronId source,
        Synapse synapse,
        Dispatch dispatch,
        SynapseReference? causedBy,
        DateTimeOffset occurredAt)
    {
        ValidateForRecording(source, synapse, dispatch);
        return Stage(source, synapse, dispatch, SynapseOriginAuthority.Internal, causedBy, occurredAt);
    }

    internal long StageIngress(
        NeuronId source,
        Synapse synapse,
        SynapseReference? causedBy,
        DateTimeOffset occurredAt)
    {
        ValidateIngress(synapse);
        return Stage(source, synapse, Dispatch.Broadcast, SynapseOriginAuthority.ExternalIngress, causedBy, occurredAt);
    }

    internal long StageDeliveryFailure(
        NeuronId source,
        DeliveryFailed deliveryFailure,
        SynapseReference causedBy,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(deliveryFailure);
        return Stage(source, deliveryFailure, Dispatch.Broadcast, SynapseOriginAuthority.Internal, causedBy, occurredAt);
    }

    private long Stage(
        NeuronId source,
        Synapse synapse,
        Dispatch dispatch,
        SynapseOriginAuthority authority,
        SynapseReference? causedBy,
        DateTimeOffset occurredAt)
    {

        var kind = router.KindOf(synapse.GetType());
        var targets = router.Resolve(source, synapse.GetType(), dispatch)
            .Select(DeliveryTarget.From)
            .ToArray();
        return journal.AppendProduced(source, kind, occurredAt, authority, causedBy, targets, serialization.Serialize(synapse));
    }
}
