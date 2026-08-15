using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Webhook;

[GenerateSerializer]
[Alias("db.webhook.delivery-conflict")]
[Description("Webhook ingress saw a delivery id with a different payload digest")]
public sealed record WebhookDeliveryConflict(
    [property: Id(0)] string Provider,
    [property: Id(1)] string SubscriptionId,
    [property: Id(2)] string DeliveryId,
    [property: Id(3)] string CanonicalPayloadDigest) : Synapse;

