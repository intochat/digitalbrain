using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class DowngradeTierRequest
{
    [JsonPropertyName("targetTierType")]
    [DataMember(Name = "targetTierType")]
    [Required]
    public UserTierType TargetTierType { get; set; }

    [JsonPropertyName("billingPeriodType")]
    [DataMember(Name = "billingPeriodType")]
    [Required]
    public BillingPeriodType BillingPeriodType { get; set; } = BillingPeriodType.Monthly;
}
