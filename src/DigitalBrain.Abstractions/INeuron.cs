namespace DigitalBrain.Abstractions;

[Alias("db.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("Deliver")]
    Task DeliverAsync(Synapse synapse);

    [Alias("ReadJournal")]
    Task<IReadOnlyList<Synapse>> ReadJournalAsync(JournalKind kind);

    [Alias("ReadJournalSnapshot")]
    Task<JournalSnapshot> ReadJournalSnapshotAsync(JournalKind kind);
}
