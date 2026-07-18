using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class DeductTokensRequest
{
    [JsonPropertyName("tokensToDeduct")]
    [DataMember(Name = "tokensToDeduct")]
    [Required(ErrorMessage = "Tokens to deduct is required.")]
    [Range(0.1, double.MaxValue, ErrorMessage = "Tokens to deduct must be greater than 0.")]
    public decimal TokensToDeduct { get; set; }

    [JsonPropertyName("serviceType")]
    [DataMember(Name = "serviceType")]
    [Required(ErrorMessage = "ServiceType to deduct is required.")]
    public ServiceType ServiceType { get; set; }

    [JsonPropertyName("requestSource")]
    [DataMember(Name = "requestSource")]
    public UsageEventSourceType? RequestSource { get; set; }
}
