namespace DigitalBrain.Mocks;

public sealed record SpotSnapshot(
    string Symbol,
    decimal Price,
    DateTimeOffset At) : Synapse;
