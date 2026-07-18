using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateSubscriptionRequest
{
    [JsonPropertyName("targetTierType")]
    [DataMember(Name = "targetTierType")]
    [Required]
    public UserTierType TargetTierType { get; set; }
}
