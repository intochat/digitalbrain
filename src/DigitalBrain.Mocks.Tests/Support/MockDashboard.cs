namespace DigitalBrain.Mocks.Tests.Support;

// Bodiless consumer for the X→dashboard declaration path (scenario 02 skeleton).
[GrainType("mockdashboard")]
public sealed class MockDashboard : Neuron, INeuron<XPostObserved>
{
    public Task HandleAsync(XPostObserved fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
