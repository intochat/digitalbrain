namespace DigitalBrain;

// The envelope. Cause = the fact whose turn produced this one (null only for edge-born
// facts; ticks carry their schedule entry's ref). Answers = Core-stamped reference to the
// ask this fact answers; modules never set it, continuation and edge matching key on it,
// never on Cause-scanning.
public sealed record SynapseMetadata(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp,
    SynapseRef? Cause,
    SynapseRef? Answers);
