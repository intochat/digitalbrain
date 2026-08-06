namespace DigitalBrain.Mocks;

public sealed class MockX : Neuron, INeuron<ObserveXPost>
{
    public Task HandleAsync(ObserveXPost command, CancellationToken cancellationToken)
    {
        Emit(new XPostObserved(command.PostId, command.Author, command.Text, command.CreatedAt));
        return Task.CompletedTask;
    }
}
