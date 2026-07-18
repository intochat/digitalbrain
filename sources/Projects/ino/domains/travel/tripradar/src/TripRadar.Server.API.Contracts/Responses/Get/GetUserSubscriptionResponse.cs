using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

/// <summary>
/// Response containing user subscription information for the billing page.
/// </summary>
public class GetUserSubscriptionResponse
{
    /// <summary>
    /// Subscription status (active, canceled, past_due, unpaid, incomplete).
    /// </summary>
    [JsonPropertyName("status")]
    [DataMember(Name = "status")]
    public string Status { get; set; } = null!;

    /// <summary>
    /// User's tier type (basic, essential, advanced).
    /// </summary>
    [JsonPropertyName("tierType")]
    [DataMember(Name = "tierType")]
    public string TierType { get; set; } = null!;

    /// <summary>
    /// Billing period (monthly, yearly).
    /// </summary>
    [JsonPropertyName("billingPeriod")]
    [DataMember(Name = "billingPeriod")]
    public string BillingPeriod { get; set; } = null!;

    /// <summary>
    /// Start date of the current billing period.
    /// </summary>
    [JsonPropertyName("currentPeriodStart")]
    [DataMember(Name = "currentPeriodStart")]
    public DateTime CurrentPeriodStart { get; set; }

    /// <summary>
    /// End date of the current billing period.
    /// </summary>
    [JsonPropertyName("currentPeriodEnd")]
    [DataMember(Name = "currentPeriodEnd")]
    public DateTime CurrentPeriodEnd { get; set; }

    /// <summary>
    /// Whether the subscription is set to cancel at period end.
    /// </summary>
    [JsonPropertyName("cancelAtPeriodEnd")]
    [DataMember(Name = "cancelAtPeriodEnd")]
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>
    /// Date when the subscription was canceled (if applicable).
    /// </summary>
    [JsonPropertyName("canceledAt")]
    [DataMember(Name = "canceledAt")]
    public DateTime? CanceledAt { get; set; }

    /// <summary>
    /// Subscription price amount in dollars.
    /// </summary>
    [JsonPropertyName("priceAmount")]
    [DataMember(Name = "priceAmount")]
    public decimal PriceAmount { get; set; }

    /// <summary>
    /// Currency code (e.g., "usd").
    /// </summary>
    [JsonPropertyName("currency")]
    [DataMember(Name = "currency")]
    public string Currency { get; set; } = null!;

    /// <summary>
    /// Date of the next invoice (if applicable).
    /// </summary>
    [JsonPropertyName("nextInvoiceDate")]
    [DataMember(Name = "nextInvoiceDate")]
    public DateTime? NextInvoiceDate { get; set; }

    /// <summary>
    /// End date of trial period (if applicable).
    /// </summary>
    [JsonPropertyName("trialEnd")]
    [DataMember(Name = "trialEnd")]
    public DateTime? TrialEnd { get; set; }

    /// <summary>
    /// Discount percentage currently applied to the subscription.
    /// </summary>
    [JsonPropertyName("discountPercent")]
    [DataMember(Name = "discountPercent")]
    public int? DiscountPercent { get; set; }

    /// <summary>
    /// Target tier type that will be applied on the next billing cycle, if downgrade is scheduled.
    /// </summary>
    [JsonPropertyName("pendingTierType")]
    [DataMember(Name = "pendingTierType")]
    public string? PendingTierType { get; set; }

    /// <summary>
    /// Date when the pending tier should become active.
    /// </summary>
    [JsonPropertyName("pendingTierEffectiveDate")]
    [DataMember(Name = "pendingTierEffectiveDate")]
    public DateTime? PendingTierEffectiveDate { get; set; }
}
