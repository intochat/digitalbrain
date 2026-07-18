namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Payment method response with card and billing details.
/// </summary>
public class PaymentMethodResponse
{
    /// <summary>Stripe payment method ID.</summary>
    public required string Id { get; init; }

    /// <summary>Payment method type (currently only 'card').</summary>
    public required string Type { get; init; }

    /// <summary>Card details.</summary>
    public required CardDetails Card { get; init; }

    /// <summary>Billing details, if available.</summary>
    public BillingDetails? BillingDetails { get; init; }

    /// <summary>Whether this is the default payment method for the subscription.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Date when the payment method was created (UTC).</summary>
    public DateTime CreatedAt { get; init; }
}
