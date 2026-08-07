namespace DigitalBrain.Product.Webhooks;

public sealed class WebhookIngressNeuron : Neuron<WebhookIngressState>, INeuron<VerifiedWebhookDeliveryReceived>
{
    public const string Kind = "webhook-ingress";

    public Task HandleAsync(VerifiedWebhookDeliveryReceived synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (!MatchesSubscription(synapse))
        {
            return Task.CompletedTask;
        }

        var state = State;
        if (!state.CanonicalPayloadDigests.TryGetValue(synapse.DeliveryId, out var existingDigest))
        {
            state.CanonicalPayloadDigests = new Dictionary<string, string>(
                state.CanonicalPayloadDigests,
                StringComparer.Ordinal)
            {
                [synapse.DeliveryId] = synapse.CanonicalPayloadDigest,
            };
            State = state;
            Emit(Accepted(synapse));
            return Task.CompletedTask;
        }

        Emit(string.Equals(existingDigest, synapse.CanonicalPayloadDigest, StringComparison.Ordinal)
            ? Duplicate(synapse)
            : Conflict(synapse));
        return Task.CompletedTask;
    }

    private bool MatchesSubscription(VerifiedWebhookDeliveryReceived delivery)
        => Origin.IsExternalIngress
            && string.Equals(Id.Name, delivery.SubscriptionId, StringComparison.Ordinal)
            && string.Equals(Origin.Source.Name, delivery.SubscriptionId, StringComparison.Ordinal);

    private static WebhookDeliveryAccepted Accepted(VerifiedWebhookDeliveryReceived delivery)
        => new(
            delivery.Provider,
            delivery.SubscriptionId,
            delivery.DeliveryId,
            delivery.CanonicalPayloadDigest);

    private static WebhookDeliveryDuplicate Duplicate(VerifiedWebhookDeliveryReceived delivery)
        => new(
            delivery.Provider,
            delivery.SubscriptionId,
            delivery.DeliveryId,
            delivery.CanonicalPayloadDigest);

    private static WebhookDeliveryConflict Conflict(VerifiedWebhookDeliveryReceived delivery)
        => new(
            delivery.Provider,
            delivery.SubscriptionId,
            delivery.DeliveryId,
            delivery.CanonicalPayloadDigest);
}
