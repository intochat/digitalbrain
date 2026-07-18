using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class UpdateMeteredBillingRequest
{
    [JsonPropertyName("enabled")]
    [DataMember(Name = "enabled")]
    [Required(ErrorMessage = "Enabled flag is required.")]
    public bool Enabled { get; set; }
}
