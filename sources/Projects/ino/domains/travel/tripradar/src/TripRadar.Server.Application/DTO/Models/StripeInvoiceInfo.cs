using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class StripeInvoiceInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Amount due in the smallest currency unit (e.g. cents).</summary>
    [JsonPropertyName("amountDue")]
    public long AmountDue { get; set; }

    /// <summary>Amount already paid in the smallest currency unit.</summary>
    [JsonPropertyName("amountPaid")]
    public long AmountPaid { get; set; }

    /// <summary>Due date for the invoice (null for auto-charge subscriptions).</summary>
    [JsonPropertyName("dueDate")]
    public DateTime? DueDate { get; set; }

    /// <summary>Timestamp when the invoice was marked paid.</summary>
    [JsonPropertyName("paidAt")]
    public DateTime? PaidAt { get; set; }

    /// <summary>URL to the hosted Stripe invoice PDF.</summary>
    [JsonPropertyName("invoicePdfUrl")]
    public string? InvoicePdfUrl { get; set; }

    /// <summary>URL to the Stripe-hosted invoice page.</summary>
    [JsonPropertyName("hostedInvoiceUrl")]
    public string? HostedInvoiceUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("cardBrand")]
    public string? CardBrand { get; set; }

    [JsonPropertyName("cardLast4")]
    public string? CardLast4 { get; set; }

    [JsonPropertyName("paymentMethodType")]
    public string? PaymentMethodType { get; set; }

    [JsonPropertyName("receiptUrl")]
    public string? ReceiptUrl { get; set; }
}
