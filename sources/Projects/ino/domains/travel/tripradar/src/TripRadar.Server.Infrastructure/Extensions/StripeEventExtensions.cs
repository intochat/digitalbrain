using Stripe;

namespace TripRadar.Server.Infrastructure.Extensions;

public static class StripeEventExtensions
{
    public static T? ExtractEventData<T>(this Event stripeEvent) where T : class, IStripeEntity => stripeEvent.Data.Object as T;
}
