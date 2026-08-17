using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Webhook;

[GenerateSerializer]
[Alias("db.webhook.verified-delivery-received")]
[Description("A verified provider webhook delivery entered the brain")]
public sealed record VerifiedWebhookDeliveryReceived(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;

