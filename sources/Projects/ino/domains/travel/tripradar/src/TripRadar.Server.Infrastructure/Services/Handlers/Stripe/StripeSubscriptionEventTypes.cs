using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

internal static class StripeSubscriptionEventTypes
{
    public static SubscriptionEventType? Resolve(string eventType) =>
        eventType switch
        {
            StripeConstants.WebhookEvents.Subscription.Created => SubscriptionEventType.SubscriptionCreated,
            StripeConstants.WebhookEvents.Subscription.Updated => SubscriptionEventType.SubscriptionUpdated,
            StripeConstants.WebhookEvents.Subscription.Deleted => SubscriptionEventType.SubscriptionDeleted,
            StripeConstants.WebhookEvents.Subscription.Canceled => SubscriptionEventType.SubscriptionCanceled,
            StripeConstants.WebhookEvents.Invoice.PaymentSucceeded => SubscriptionEventType.SubscriptionUpdated,
            StripeConstants.WebhookEvents.CheckoutSession.Completed => SubscriptionEventType.SubscriptionCreated,
            _ => null
        };
}
