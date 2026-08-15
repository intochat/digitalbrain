namespace DigitalBrain.Mcp;

internal sealed record NeuronJournalPage(
    string Neuron,
    string Kind,
    long ResumeSequence,
    bool Compacted,
    IReadOnlyList<JournaledSynapse> Entries);

