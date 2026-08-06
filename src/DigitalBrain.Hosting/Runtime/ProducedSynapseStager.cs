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

    internal long StageAuthored(
        NeuronId source,
        Synapse synapse,
        SynapseReference? causedBy,
        DateTimeOffset occurredAt)
    {
        ValidateAuthored(synapse);
        return Stage(source, synapse, causedBy, occurredAt);
    }

    internal long StageDeliveryFailure(
        NeuronId source,
        DeliveryFailed deliveryFailure,
        SynapseReference causedBy,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(deliveryFailure);
        return Stage(source, deliveryFailure, causedBy, occurredAt);
    }

    private long Stage(
        NeuronId source,
        Synapse synapse,
        SynapseReference? causedBy,
        DateTimeOffset occurredAt)
    {

        var kind = router.KindOf(synapse.GetType());
        var targets = router.Resolve(source, synapse.GetType())
            .Select(DeliveryTarget.From)
            .ToArray();
        return journal.AppendProduced(source, kind, occurredAt, causedBy, targets, serialization.Serialize(synapse));
    }
}
