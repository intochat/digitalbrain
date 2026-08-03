namespace DigitalBrain;

public sealed record SynapseMetadata(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp);
