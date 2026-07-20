namespace DigitalBrain.Abstractions;

[Alias("db.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("Deliver")]
    Task DeliverAsync(SynapseDelivery delivery);

    [Alias("ReadJournal")]
    Task<JournalRead> ReadJournalAsync(JournalKind kind, long afterSequence);

    [Alias("Watch")]
    Task WatchAsync(JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias("Unwatch")]
    Task UnwatchAsync(IJournalObserver observer);
}
