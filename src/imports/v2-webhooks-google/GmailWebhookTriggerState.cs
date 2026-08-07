namespace DigitalBrain.Product.Google;

public sealed class GmailWebhookTriggerState
{
    public IReadOnlyDictionary<string, GmailWebhookDeliveryMapping> Deliveries { get; set; }
        = new Dictionary<string, GmailWebhookDeliveryMapping>(StringComparer.Ordinal);
}
