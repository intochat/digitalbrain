namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Billing address details for a payment method.
/// </summary>
public class BillingDetails
{
    /// <summary>Cardholder name.</summary>
    public string? Name { get; init; }

    /// <summary>Billing email.</summary>
    public string? Email { get; init; }

    /// <summary>Billing country (ISO 3166-1 alpha-2).</summary>
    public string? Country { get; init; }

    /// <summary>Billing postal/zip code.</summary>
    public string? PostalCode { get; init; }
}
