using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

/// <summary>
/// Subscription details from Stripe.
/// </summary>
public class StripeSubscriptionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("currentPeriodStart")]
    public DateTime CurrentPeriodStart { get; set; }

    [JsonPropertyName("currentPeriodEnd")]
    public DateTime CurrentPeriodEnd { get; set; }

    [JsonPropertyName("cancelAtPeriodEnd")]
    public bool CancelAtPeriodEnd { get; set; }

    [JsonPropertyName("canceledAt")]
    public DateTime? CanceledAt { get; set; }

    [JsonPropertyName("priceAmount")]
    public int PriceAmount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "usd";

    [JsonPropertyName("priceId")]
    public string? PriceId { get; set; }

    [JsonPropertyName("billingPeriod")]
    public string BillingPeriod { get; set; } = "monthly";

    [JsonPropertyName("trialEnd")]
    public DateTime? TrialEnd { get; set; }

    [JsonPropertyName("discountPercent")]
    public int? DiscountPercent { get; set; }

    [JsonPropertyName("defaultPaymentMethodId")]
    public string? DefaultPaymentMethodId { get; set; }
}
