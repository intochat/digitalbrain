namespace DigitalBrain.Mocks;

public sealed record WebSearchCompleted(
    string Query,
    string Domain,
    string Snippet,
    string Source) : Synapse;
