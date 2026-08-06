namespace DigitalBrain.Mocks;

public sealed record ObserveEmail(
    string MessageId,
    string From,
    string Domain,
    string Subject,
    string Snippet) : Synapse;

public sealed record EmailReceived(
    string MessageId,
    string From,
    string Domain,
    string Subject,
    string Snippet) : Synapse;

[GrainType("mockgmail")]
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
