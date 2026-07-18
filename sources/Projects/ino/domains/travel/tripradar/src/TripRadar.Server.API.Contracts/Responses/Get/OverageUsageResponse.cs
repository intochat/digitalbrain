using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class OverageUsageResponse
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("tierName")]
    public string TierName { get; set; } = null!;

    [JsonPropertyName("regularTokensUsed")]
    public decimal RegularTokensUsed { get; set; }

    [JsonPropertyName("overageTokensUsed")]
    public decimal OverageTokensUsed { get; set; }

    [JsonPropertyName("totalOverageCharges")]
    public decimal TotalOverageCharges { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    public int Month { get; set; }

    [JsonPropertyName("isEligibleForOverage")]
    public bool IsEligibleForOverage { get; set; }

    [JsonPropertyName("payAsYouGoEnabled")]
    public bool PayAsYouGoEnabled { get; set; }
}
