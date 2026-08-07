namespace DigitalBrain.Product.Webhooks;

public sealed class WebhookIngressState
{
    public IReadOnlyDictionary<string, string> CanonicalPayloadDigests { get; set; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
}
