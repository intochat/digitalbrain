using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateSubscriptionCheckoutRequest
{
    [Required]
    [JsonPropertyName("targetTierType")]
    [DataMember(Name = "targetTierType")]
    public UserTierType TargetTierType { get; set; }

    [JsonPropertyName("billingPeriodType")]
    [DataMember(Name = "billingPeriodType")]
    public BillingPeriodType BillingPeriodType { get; set; } = BillingPeriodType.Monthly;

    [JsonPropertyName("promoCode")]
    [DataMember(Name = "promoCode")]
    public string? PromoCode { get; set; }
}
