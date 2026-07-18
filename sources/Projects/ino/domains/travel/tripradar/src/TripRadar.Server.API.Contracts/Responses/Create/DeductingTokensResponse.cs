using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class DeductingTokensResponse
{
    [JsonPropertyName("remainingTokens")]
    [DataMember(Name = "remainingTokens")]
    [Required]
    public decimal RemainingTokens { get; set; }

    [JsonPropertyName("dailyLimit")]
    [DataMember(Name = "dailyLimit")]
    [Required]
    public decimal MonthlyLimit { get; set; }

    [JsonPropertyName("tokensDeducted")]
    [DataMember(Name = "tokensDeducted")]
    [Required]
    public decimal TokensDeducted { get; set; }

    [JsonPropertyName("limitReached")]
    [DataMember(Name = "limitReached")]
    [Required]
    public bool LimitReached { get; set; }

    [JsonPropertyName("canUseApi")]
    [DataMember(Name = "canUseApi")]
    [Required]
    public bool CanUseApi { get; set; }
}
