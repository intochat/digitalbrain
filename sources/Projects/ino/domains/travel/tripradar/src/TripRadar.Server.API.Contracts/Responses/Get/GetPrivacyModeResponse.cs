using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetPrivacyModeResponse
{
    [JsonPropertyName("enabled")]
    [DataMember(Name = "enabled")]
    [Required]
    public bool Enabled { get; set; }
}
