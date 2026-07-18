namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Detailed subscription information response.
/// </summary>
public class SubscriptionResponse
{
    /// <summary>Stripe subscription ID.</summary>
    public required string Id { get; init; }

    /// <summary>Subscription status: active, canceled, past_due, unpaid, incomplete.</summary>
    public required string Status { get; init; }

    /// <summary>Current period start date (UTC).</summary>
    public DateTime CurrentPeriodStart { get; init; }

    /// <summary>Current period end date (UTC).</summary>
    public DateTime CurrentPeriodEnd { get; init; }

    /// <summary>Whether subscription is set to cancel at period end.</summary>
    public bool CancelAtPeriodEnd { get; init; }

    /// <summary>Date when cancellation was requested (UTC), if applicable.</summary>
    public DateTime? CanceledAt { get; init; }

    /// <summary>Price amount in cents.</summary>
    public long PriceAmount { get; init; }

    /// <summary>Currency code (e.g., 'usd').</summary>
    public required string Currency { get; init; }

    /// <summary>Next billing date (UTC), if subscription is active.</summary>
    public DateTime? NextBillingDate { get; init; }

    /// <summary>Trial period end date (UTC), if applicable.</summary>
    public DateTime? TrialEnd { get; init; }

    /// <summary>Discount percentage, if a coupon/discount is applied.</summary>
    public decimal? DiscountPercent { get; init; }

    /// <summary>Stripe price ID for the current subscription item.</summary>
    public string? PriceId { get; init; }

    /// <summary>Stripe product ID associated with the subscription.</summary>
    public string? ProductId { get; init; }

    /// <summary>Default payment method ID for this subscription.</summary>
    public string? DefaultPaymentMethodId { get; init; }
}
