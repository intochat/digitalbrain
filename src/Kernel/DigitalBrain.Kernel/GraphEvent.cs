namespace DigitalBrain.Kernel;

internal sealed record GraphEvent(
    long Sequence,
    string Kind,
    string ConnectionId,
    string? Source,
    string? SynapseAlias,
    string? Target,
    DateTimeOffset Timestamp);
