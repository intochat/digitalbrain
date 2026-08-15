using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Webhook;

[GenerateSerializer]
[Alias("db.webhook.delivery-accepted")]
[Description("Webhook ingress accepted a delivery and retained its digest")]
public sealed record WebhookDeliveryAccepted(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;

