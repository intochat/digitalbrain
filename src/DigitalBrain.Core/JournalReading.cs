namespace DigitalBrain;

public sealed record JournalFact(
    long Position,
    string Entry,
    string Kind,
    SynapseMetadata Metadata,
    SynapseRef? Cause,
    SynapseRef? Answers,
    IReadOnlyList<Delivery>? To,
    Synapse? Body);

public sealed record Delivery(NeuronId Receiver, string Via);

public sealed record NeuronReading(
    IReadOnlyList<JournalFact> Journal,
    IReadOnlyDictionary<string, IReadOnlyList<NeuronId>> Connections);
