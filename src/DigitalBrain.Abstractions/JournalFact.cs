namespace DigitalBrain;

// The public read shape (edge, tests, introspection — flow 10). One record per journal
// line: Position = the entry's own sequence, Entry = "heard" | "said", To = the said
// entry's receiver snapshot with per-receiver provenance ("declared" | "connected" |
// "ask"). Body is null when the line's kind is not in the running catalog: journals
// outlive code, reads never throw. Connections ride beside the journal so per-instance
// introspection never lies, before or after compaction.
public sealed record JournalFact(
    long Position,
    string Entry,
    string Kind,
    SynapseMetadata Metadata,
    IReadOnlyList<Delivery>? To,
    Synapse? Body);

public sealed record Delivery(NeuronId Receiver, string Via);

public sealed record NeuronReading(
    IReadOnlyList<JournalFact> Journal,
    IReadOnlyDictionary<string, IReadOnlyList<NeuronId>> Connections);
