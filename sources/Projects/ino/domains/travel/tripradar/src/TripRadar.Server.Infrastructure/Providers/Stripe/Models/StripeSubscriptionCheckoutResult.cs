namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

public class StripeSubscriptionCheckoutResult
{
    public string? ClientSecret { get; set; }

    public long AmountSubtotal { get; set; }

    public long AmountDiscount { get; set; }

    public long AmountTotal { get; set; }

    public string Currency { get; set; } = string.Empty;
}