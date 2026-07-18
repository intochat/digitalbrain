using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

/// <summary>
/// DTO for user subscription details.
/// </summary>
public class UserSubscriptionDTO
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("tierType")]
    public string TierType { get; set; } = null!;

    [JsonPropertyName("billingPeriod")]
    public string BillingPeriod { get; set; } = null!;

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
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("nextInvoiceDate")]
    public DateTime? NextInvoiceDate { get; set; }

    [JsonPropertyName("trialEnd")]
    public DateTime? TrialEnd { get; set; }

    [JsonPropertyName("discountPercent")]
    public int? DiscountPercent { get; set; }

    [JsonPropertyName("pendingTierType")]
    public string? PendingTierType { get; set; }

    [JsonPropertyName("pendingTierEffectiveDate")]
    public DateTime? PendingTierEffectiveDate { get; set; }
}
