namespace DigitalBrain.Mocks;

public sealed record ObserveXPost(
    string PostId,
    string Author,
    string Text,
    DateTimeOffset CreatedAt) : Synapse;

public sealed record XPostObserved(
    string PostId,
    string Author,
    string Text,
    DateTimeOffset CreatedAt) : Synapse;

public sealed class MockX : Neuron, INeuron<ObserveXPost>
{
    public Task HandleAsync(ObserveXPost command, CancellationToken cancellationToken)
    {
        Emit(new XPostObserved(command.PostId, command.Author, command.Text, command.CreatedAt));
        return Task.CompletedTask;
    }
}
