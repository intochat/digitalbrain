namespace DigitalBrain.Mocks;

public sealed class MockCrypto : Neuron, INeuron<ObserveSpot>
{
    public Task HandleAsync(ObserveSpot command, CancellationToken cancellationToken)
    {
        Emit(new SpotSnapshot(command.Symbol, command.Price, command.At));
        return Task.CompletedTask;
    }
}
