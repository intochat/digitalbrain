using DigitalBrain.Product.Enrichment;

namespace DigitalBrain.Product.Google;

/// <summary>
/// Relays Hosting's terminal delivery outcome to the Gmail trigger without requiring a host to deliver to itself.
/// </summary>
public sealed class GmailWebhookDeliveryFailureObserver : Neuron, INeuron<DeliveryFailed>
{
    public const string Kind = "gmail-webhook-delivery-failure-observer";

    public Task HandleAsync(DeliveryFailed synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var trigger = new NeuronId(GmailWebhookTriggerNeuron.Kind, Id.Name);
        if (!Equals(Origin.Source, trigger)
            || !Equals(synapse.Synapse.Source, trigger)
            || !string.Equals(synapse.Receiver.Kind, AccountEnrichmentNeuron.Kind, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Emit(
            new GmailWebhookStartDeliveryFailed(synapse.Synapse, synapse.Receiver),
            Dispatch.Direct(trigger));
        return Task.CompletedTask;
    }
}
