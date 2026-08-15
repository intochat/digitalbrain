namespace DigitalBrain.Mcp;

internal sealed record JournaledSynapse(
    long Sequence,
    string Synapse,
    string Caller,
    string Correlation,
    DateTimeOffset Timestamp);

