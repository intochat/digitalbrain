namespace DigitalBrain.Mocks.Tests;

// Bodiless consumer for the X→dashboard declaration path (scenario 02 skeleton).
public sealed class MockDashboard : Neuron, INeuron<XPostObserved>
{
    public Task HandleAsync(XPostObserved fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
