namespace DigitalBrain.Mocks;

public sealed record ObserveXPost(
    string PostId,
    string Author,
    string Text,
    DateTimeOffset CreatedAt) : Synapse;
