namespace DigitalBrain;

public sealed class AskFailedException : Exception
{
    internal AskFailedException(NeuronId session, SynapseRef ask, DeliveryFailed failure)
        : base($"Ask {ask.Source}/{ask.Sequence} on session {session} failed: delivery to "
            + $"{failure.Receiver} — {failure.Reason} (attempts: {failure.Attempts}).")
        => Fact = failure;

    internal AskFailedException(NeuronId session, SynapseRef ask, AskExpired expired)
        : base($"Ask {ask.Source}/{ask.Sequence} on session {session} expired: "
            + $"'{expired.Question}' was never answered inside the ask horizon.")
        => Fact = expired;

    public Synapse Fact { get; }
}
