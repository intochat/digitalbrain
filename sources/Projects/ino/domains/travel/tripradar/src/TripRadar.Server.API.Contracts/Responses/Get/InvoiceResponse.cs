using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class InvoiceResponse
{
    [JsonPropertyName("cursor")]
    [DataMember(Name = "cursor")]
    public string Cursor { get; set; } = null!;

    [JsonPropertyName("number")]
    [DataMember(Name = "number")]
    public string? Number { get; set; }

    [JsonPropertyName("status")]
    [DataMember(Name = "status")]
    public string? Status { get; set; }

    [JsonPropertyName("currency")]
    [DataMember(Name = "currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("createdAt")]
    [DataMember(Name = "createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("amountDue")]
    [DataMember(Name = "amountDue")]
    public decimal AmountDue { get; set; }

    [JsonPropertyName("amountPaid")]
    [DataMember(Name = "amountPaid")]
    public decimal AmountPaid { get; set; }

    /// <summary>Due date for the invoice (null for auto-charge subscriptions).</summary>
    [JsonPropertyName("dueDate")]
    [DataMember(Name = "dueDate")]
    public DateTime? DueDate { get; set; }

    /// <summary>Timestamp when the invoice was marked paid.</summary>
    [JsonPropertyName("paidAt")]
    [DataMember(Name = "paidAt")]
    public DateTime? PaidAt { get; set; }

    /// <summary>URL to the hosted Stripe invoice PDF.</summary>
    [JsonPropertyName("invoicePdfUrl")]
    [DataMember(Name = "invoicePdfUrl")]
    public string? InvoicePdfUrl { get; set; }

    /// <summary>URL to the Stripe-hosted invoice page.</summary>
    [JsonPropertyName("hostedInvoiceUrl")]
    [DataMember(Name = "hostedInvoiceUrl")]
    public string? HostedInvoiceUrl { get; set; }

    [JsonPropertyName("description")]
    [DataMember(Name = "description")]
    public string? Description { get; set; }

    [JsonPropertyName("subscriptionId")]
    [DataMember(Name = "subscriptionId")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("cardBrand")]
    [DataMember(Name = "cardBrand")]
    public string? CardBrand { get; set; }

    [JsonPropertyName("cardLast4")]
    [DataMember(Name = "cardLast4")]
    public string? CardLast4 { get; set; }

    [JsonPropertyName("paymentMethodType")]
    [DataMember(Name = "paymentMethodType")]
    public string? PaymentMethodType { get; set; }

    [JsonPropertyName("receiptUrl")]
    [DataMember(Name = "receiptUrl")]
    public string? ReceiptUrl { get; set; }
}
