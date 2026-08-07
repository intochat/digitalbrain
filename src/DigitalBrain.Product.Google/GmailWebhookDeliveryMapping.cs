using DigitalBrain.Product.Enrichment;

namespace DigitalBrain.Product.Google;

public sealed record GmailWebhookDeliveryMapping(
    string Provider,
    string SubscriptionId,
    string DeliveryId,
    string CanonicalPayloadDigest,
    AccountEnrichmentRequest? Request,
    GmailWebhookStartStatus StartStatus);
