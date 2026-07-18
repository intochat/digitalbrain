using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class AirlineResponse
{
    [JsonPropertyName("airlineCode")]
    [DataMember(Name = "airlineCode")]
    [Required]
    public string AirlineCode { get; set; } = null!;

    [JsonPropertyName("airlineName")]
    [DataMember(Name = "airlineName")]
    [Required]
    public string AirlineName { get; set; } = null!;

    [JsonPropertyName("isAlliance")]
    [DataMember(Name = "isAlliance")]
    public bool IsAlliance { get; set; }

    [JsonPropertyName("logoUrl")]
    [DataMember(Name = "logoUrl")]
    public string? LogoUrl { get; set; }
}
