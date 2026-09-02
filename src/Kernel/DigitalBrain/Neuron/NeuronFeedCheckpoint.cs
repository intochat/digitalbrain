namespace DigitalBrain.Core;

internal readonly record struct NeuronFeedCheckpoint(
    IReadOnlyList<byte[]> Retained,
    IReadOnlyDictionary<string, long> Tallies,
    long LastSequence);
