namespace DigitalBrain.Abstractions;

[Alias("db.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("Deliver")]
    Task DeliverAsync(Synapse synapse);

    [Alias("ReadJournal")]
    Task<JournalRead> ReadJournalAsync(JournalKind kind, long afterSequence);
}
