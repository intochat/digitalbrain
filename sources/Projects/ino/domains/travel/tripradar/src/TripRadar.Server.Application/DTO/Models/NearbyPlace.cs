using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class NearbyPlace
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("transportations")]
    public List<Transportation>? Transportations { get; set; }
}
