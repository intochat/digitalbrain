using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class ServiceInfo
{
    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    [Required]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [DataMember(Name = "description")]
    [Required]
    public string Description { get; set; } = string.Empty;
}
