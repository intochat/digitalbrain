using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Stripe;

[GenerateSerializer]
public sealed record IncomingWebhookEnvelope([property: Id(1)] string PayloadJson,
    [property: Id(2)] string Signature
) : Synapse;

[GenerateSerializer]
public sealed record SubscriptionActivated([property: Id(1)] string UserId,
    [property: Id(2)] string PriceId,
    [property: Id(3)] string SubscriptionId
) : Synapse;

[GenerateSerializer]
public sealed record WebhookVerified([property: Id(1)] string EventType,
    [property: Id(2)] string EventId
) : Synapse;

[GenerateSerializer]
public sealed record WebhookRejected([property: Id(1)] string Reason
) : Synapse;

[GenerateSerializer]
public sealed record SubscriptionUpdated([property: Id(1)] string UserId,
    [property: Id(2)] string PriceId,
    [property: Id(3)] string SubscriptionId,
    [property: Id(4)] string Status
) : Synapse;

[GenerateSerializer]
public sealed record SubscriptionCancelled([property: Id(1)] string UserId,
    [property: Id(2)] string SubscriptionId
) : Synapse;
