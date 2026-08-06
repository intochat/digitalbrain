namespace DigitalBrain;

public sealed record SynapseMetadata(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp);

public readonly record struct SynapseRef(NeuronId Source, long Sequence);

public sealed record DeliveryFailed(
    SynapseRef Fact,
    NeuronId Receiver,
    string Reason,
    int Attempts) : Synapse;

public sealed record JournalFact(
    long Position,
    string Entry,
    string Kind,
    SynapseMetadata Metadata,
    SynapseRef? Cause,
    IReadOnlyList<NeuronId>? To,
    Synapse? Body);

public sealed record NeuronReading(IReadOnlyList<JournalFact> Journal);
