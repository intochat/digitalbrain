namespace DigitalBrain;

public sealed record SynapseOrigin(
    NeuronId Source,
    long Sequence,
    DateTimeOffset OccurredAt);
