using DigitalBrain.Mocks;

namespace DigitalBrain.Testing.Mechanics;

public sealed class MockDashboard : Neuron, INeuron<XPostObserved>
{
    public Task HandleAsync(XPostObserved synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
