using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Sdk.Webhook;

[GenerateSerializer]
[Alias("db.webhook.verified-delivery-received")]
[Description("A verified provider webhook delivery entered the brain")]
public sealed record VerifiedWebhookDeliveryReceived(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;

[GenerateSerializer]
[Alias("db.webhook.delivery-accepted")]
[Description("Webhook ingress accepted a delivery and retained its digest")]
public sealed record WebhookDeliveryAccepted(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;

[GenerateSerializer]
[Alias("db.webhook.delivery-duplicate")]
[Description("Webhook ingress saw a delivery id with the same payload digest")]
public sealed record WebhookDeliveryDuplicate(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;

[GenerateSerializer]
[Alias("db.webhook.delivery-conflict")]
[Description("Webhook ingress saw a delivery id with a different payload digest")]
public sealed record WebhookDeliveryConflict(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;
