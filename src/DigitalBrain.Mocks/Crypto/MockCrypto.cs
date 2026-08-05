namespace DigitalBrain.Mocks;

public sealed record ObserveSpot(
    string Symbol,
    decimal Price,
    DateTimeOffset At) : Synapse;

public sealed record SpotSnapshot(
    string Symbol,
    decimal Price,
    DateTimeOffset At) : Synapse;

public sealed class MockCrypto : Neuron, INeuron<ObserveSpot>
{
    public Task HandleAsync(ObserveSpot command, CancellationToken cancellationToken)
    {
        Emit(new SpotSnapshot(command.Symbol, command.Price, command.At));
        return Task.CompletedTask;
    }
}
