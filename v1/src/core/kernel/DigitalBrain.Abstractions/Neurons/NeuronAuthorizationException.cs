namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.authorization-error")]
// TryDeliverAsync records a refusal and consumes the outbox entry, so the sender is done with the
// delivery. Retracting the receiver's inbound cause as well would erase every trace that the fact
// ever arrived at the neuron that refused it.
[SettledDeliveryFailure]
public sealed class NeuronAuthorizationException : Exception
{
    public NeuronAuthorizationException()
        : this("The caller is not authorized to reach this neuron.")
    {
    }

    public NeuronAuthorizationException(string message)
        : base(message)
    {
    }

    public NeuronAuthorizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
