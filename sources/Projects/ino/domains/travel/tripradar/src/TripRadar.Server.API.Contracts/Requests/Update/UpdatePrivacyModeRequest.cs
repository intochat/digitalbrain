using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public sealed class UpdatePrivacyModeRequest
{
    [JsonPropertyName("enabled")]
    [DataMember(Name = "enabled")]
    [Required]
    public bool Enabled { get; set; }
}
