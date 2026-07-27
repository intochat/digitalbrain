namespace DigitalBrain.Mcp;

internal sealed record NeuronJournalPage(
    string Neuron,
    string Kind,
    long ResumeSequence,
    bool Compacted,
    IReadOnlyList<JournaledSynapse> Entries);

internal sealed record JournaledSynapse(
    long Sequence,
    string Synapse,
    string Caller,
    string Correlation,
    DateTimeOffset Timestamp);

internal sealed record ActiveNeuron(string GrainType, string Identity, string Silo);

internal sealed record ChatTranscriptPage(
    string Chat,
    IReadOnlyList<ChatTranscriptTurn> Turns);

internal sealed record ChatTranscriptTurn(string Speaker, string Text);
