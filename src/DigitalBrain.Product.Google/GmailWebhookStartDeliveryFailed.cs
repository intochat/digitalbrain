namespace DigitalBrain.Product.Google;

/// <summary>
/// Google-internal notification that a Gmail-triggered enrichment start exhausted delivery.
/// </summary>
public sealed record GmailWebhookStartDeliveryFailed : Synapse
{
    public GmailWebhookStartDeliveryFailed(SynapseReference failedStart, NeuronId receiver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failedStart.Source.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(failedStart.Source.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failedStart.Sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiver.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiver.Name);

        FailedStart = failedStart;
        Receiver = receiver;
    }

    public SynapseReference FailedStart { get; }

    public NeuronId Receiver { get; }
}
