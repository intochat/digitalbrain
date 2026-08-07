namespace DigitalBrain.Product.Webhooks;

public sealed record WebhookDeliveryConflict : Synapse
{
    public WebhookDeliveryConflict(
        string provider,
        string subscriptionId,
        string deliveryId,
        string canonicalPayloadDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPayloadDigest);

        Provider = provider.Trim();
        SubscriptionId = subscriptionId.Trim();
        DeliveryId = deliveryId.Trim();
        CanonicalPayloadDigest = canonicalPayloadDigest.Trim();
    }

    public string Provider { get; }

    public string SubscriptionId { get; }

    public string DeliveryId { get; }

    public string CanonicalPayloadDigest { get; }
}
