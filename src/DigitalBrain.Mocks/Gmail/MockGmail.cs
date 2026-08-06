namespace DigitalBrain.Mocks;

public sealed class MockGmail : Neuron, INeuron<ObserveEmail>
{
    public Task HandleAsync(ObserveEmail command, CancellationToken cancellationToken)
    {
        Emit(new EmailReceived(
            command.MessageId,
            command.From,
            command.Domain,
            command.Subject,
            command.Snippet));
        return Task.CompletedTask;
    }
}
