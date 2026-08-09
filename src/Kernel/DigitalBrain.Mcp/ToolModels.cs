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

internal sealed record ActiveNeuron(string GrainType, string Identity);

internal sealed record ChatTranscriptPage(string Chat, IReadOnlyList<ChatTranscriptTurn> Turns);

internal sealed record ChatTranscriptTurn(string Speaker, string Text);

internal sealed record ChatMessageResult(
    string Chat,
    string CommandId,
    string CorrelationId,
    string Response,
    long Sequence,
    DateTimeOffset Timestamp,
    IReadOnlyList<ChatButtonOfferResult>? Buttons = null,
    IReadOnlyList<ChatChartOfferResult>? Charts = null);

internal sealed record ChatButtonOfferResult(string ButtonId, string Label, string Action);

internal sealed record ChatChartOfferResult(
    string Title,
    IReadOnlyList<ChatChartPointResult> Points,
    string ChartKind);

internal sealed record ChatChartPointResult(string Label, double Value);
