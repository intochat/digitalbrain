namespace DigitalBrain.Mocks;

public sealed record XPostObserved(
    string PostId,
    string Author,
    string Text,
    DateTimeOffset CreatedAt) : Synapse;
