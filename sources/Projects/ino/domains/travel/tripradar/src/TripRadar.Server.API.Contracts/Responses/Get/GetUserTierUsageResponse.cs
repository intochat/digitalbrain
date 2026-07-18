using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetUserTierUsageResponse
{
    [JsonPropertyName("tierName")]
    [DataMember(Name = "tierName")]
    [Required]
    public string TierName { get; set; } = null!;

    [JsonPropertyName("currentUsage")]
    [DataMember(Name = "currentUsage")]
    [Required]
    public int CurrentUsage { get; set; }

    [JsonPropertyName("dailyLimit")]
    [DataMember(Name = "dailyLimit")]
    [Required]
    public int DailyLimit { get; set; }

    [JsonPropertyName("remainingRequests")]
    [DataMember(Name = "remainingRequests")]
    [Required]
    public int RemainingRequests { get; set; }

    [JsonPropertyName("usagePercentage")]
    [DataMember(Name = "usagePercentage")]
    [Required]
    public double UsagePercentage { get; set; }
}
