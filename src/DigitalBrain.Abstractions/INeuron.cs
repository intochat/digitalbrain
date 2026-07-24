namespace DigitalBrain.Abstractions;

[Alias("db.neuron")]
public partial interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    Task Deliver(SynapseDelivery delivery);

    [Alias(nameof(ReadJournal))]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);

    [Alias(nameof(Watch))]
    Task Watch(JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias(nameof(Unwatch))]
    Task Unwatch(IJournalObserver observer);
}
