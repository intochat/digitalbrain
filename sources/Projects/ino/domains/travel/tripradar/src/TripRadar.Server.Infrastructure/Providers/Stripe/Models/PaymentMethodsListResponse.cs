namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Response containing all payment methods for a customer.
/// </summary>
public class PaymentMethodsListResponse
{
    /// <summary>List of payment methods.</summary>
    public required List<PaymentMethodResponse> PaymentMethods { get; init; }

    /// <summary>Whether the customer has an active subscription.</summary>
    public bool HasActiveSubscription { get; init; }

    /// <summary>ID of the default payment method, if any.</summary>
    public string? DefaultPaymentMethodId { get; init; }
}
