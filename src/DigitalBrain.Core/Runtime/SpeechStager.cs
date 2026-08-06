namespace DigitalBrain;

internal sealed class SpeechStager(Journal journal, Router router, ISynapseCodec codec)
{
    internal long Stage(NeuronId source, Synapse fact, SynapseRefEntry? cause, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var kind = router.KindOf(fact.GetType());
        var receivers = router.Resolve(source, fact.GetType())
            .Select(DeliveryTarget.From)
            .ToArray();
        return journal.AppendSaid(kind, now, cause, receivers, codec.Encode(fact), SpeechRole.Fanout);
    }
}
