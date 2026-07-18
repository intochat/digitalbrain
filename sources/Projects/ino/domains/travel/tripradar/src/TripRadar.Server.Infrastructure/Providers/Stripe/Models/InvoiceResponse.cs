namespace TripRadar.Server.Infrastructure.Providers.Stripe.Models;

/// <summary>
/// Invoice response for payment history.
/// </summary>
public class InvoiceResponse
{
    /// <summary>Stripe invoice ID.</summary>
    public required string Id { get; init; }

    /// <summary>Invoice number.</summary>
    public string? Number { get; init; }

    /// <summary>Invoice status: draft, open, paid, void, uncollectible.</summary>
    public required string Status { get; init; }

    /// <summary>Invoice amount in cents.</summary>
    public long AmountDue { get; init; }

    /// <summary>Amount paid in cents.</summary>
    public long AmountPaid { get; init; }

    /// <summary>Currency code (e.g., 'usd').</summary>
    public required string Currency { get; init; }

    /// <summary>Invoice creation date (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Invoice due date (UTC), if applicable.</summary>
    public DateTime? DueDate { get; init; }

    /// <summary>Date when invoice was paid (UTC), if applicable.</summary>
    public DateTime? PaidAt { get; init; }

    /// <summary>URL to download the invoice PDF.</summary>
    public string? InvoicePdfUrl { get; init; }

    /// <summary>URL to view the hosted invoice page.</summary>
    public string? HostedInvoiceUrl { get; init; }

    /// <summary>Description/memo for the invoice.</summary>
    public string? Description { get; init; }

    /// <summary>Subscription ID associated with this invoice, if any.</summary>
    public string? SubscriptionId { get; init; }

    /// <summary>Card brand used for payment (if available).</summary>
    public string? CardBrand { get; init; }

    /// <summary>Last 4 digits of the payment card (if available).</summary>
    public string? CardLast4 { get; init; }

    /// <summary>Payment method type used for the invoice payment.</summary>
    public string? PaymentMethodType { get; init; }

    /// <summary>URL to the payment receipt (if available).</summary>
    public string? ReceiptUrl { get; init; }
}
