namespace DigitalBrain.Mocks;

public sealed record ObserveSpot(
    string Symbol,
    decimal Price,
    DateTimeOffset At) : Synapse;
