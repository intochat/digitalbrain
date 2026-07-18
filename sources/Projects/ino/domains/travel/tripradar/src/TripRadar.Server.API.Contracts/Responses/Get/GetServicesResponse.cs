using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class GetServicesResponse
{
    [JsonPropertyName("services")]
    [DataMember(Name = "services")]
    [Required]
    public List<ServiceInfo> Services { get; set; } = new();
}
