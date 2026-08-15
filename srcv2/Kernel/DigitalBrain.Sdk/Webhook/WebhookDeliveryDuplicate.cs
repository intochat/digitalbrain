using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Webhook;

[GenerateSerializer]
[Alias("db.webhook.delivery-duplicate")]
[Description("Webhook ingress saw a delivery id with the same payload digest")]
public sealed record WebhookDeliveryDuplicate(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;

