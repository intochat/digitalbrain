namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Response after detaching a payment method.
/// </summary>
public class DetachPaymentMethodResponse
{
    /// <summary>Success message.</summary>
    public required string Message { get; init; }

    /// <summary>ID of the new default payment method, if one was auto-assigned.</summary>
    public string? NewDefaultPaymentMethodId { get; init; }

    /// <summary>Number of remaining payment methods.</summary>
    public int RemainingPaymentMethods { get; init; }
}
