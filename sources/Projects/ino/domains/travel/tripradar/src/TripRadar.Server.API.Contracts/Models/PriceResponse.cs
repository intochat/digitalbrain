using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PriceResponse
{
    [JsonPropertyName("amount")]
    [DataMember(Name = "amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    [DataMember(Name = "currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("tierName")]
    [DataMember(Name = "tierName")]
    public string TierName { get; set; } = string.Empty;

    [JsonPropertyName("tokensPerMonthLimit")]
    [DataMember(Name = "tokensPerMonthLimit")]
    public decimal TokensPerMonthLimit { get; set; }

    [JsonPropertyName("billingPeriodName")]
    [DataMember(Name = "billingPeriodName")]
    public string BillingPeriodName { get; set; } = string.Empty;
}
