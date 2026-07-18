namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Card details for a payment method.
/// </summary>
public class CardDetails
{
    /// <summary>Card brand: visa, mastercard, amex, discover, diners, jcb, unionpay.</summary>
    public required string Brand { get; init; }

    /// <summary>Last 4 digits of the card number.</summary>
    public required string Last4 { get; init; }

    /// <summary>Card expiration month (1-12).</summary>
    public int ExpMonth { get; init; }

    /// <summary>Card expiration year (e.g., 2024).</summary>
    public int ExpYear { get; init; }

    /// <summary>Country where the card was issued (ISO 3166-1 alpha-2).</summary>
    public string? Country { get; init; }
}
