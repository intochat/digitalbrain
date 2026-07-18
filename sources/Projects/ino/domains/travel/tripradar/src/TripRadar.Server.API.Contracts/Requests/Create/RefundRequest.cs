using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class RefundRequest
{
    [Required]
    [JsonPropertyName("reason")]
    [DataMember(Name = "reason")]
    public RefundReasonType ReasonType { get; set; }

    [JsonPropertyName("metadata")]
    [DataMember(Name = "metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
