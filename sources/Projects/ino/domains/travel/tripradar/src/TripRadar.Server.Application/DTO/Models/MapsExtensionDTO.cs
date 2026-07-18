using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsExtensionDTO
{
    [JsonPropertyName("highlights")]
    public List<string>? Highlights { get; set; }

    [JsonPropertyName("popular_for")]
    public List<string>? PopularFor { get; set; }

    [JsonPropertyName("accessibility")]
    public List<string>? Accessibility { get; set; }

    [JsonPropertyName("crowd")]
    public List<string>? Crowd { get; set; }

    [JsonPropertyName("payments")]
    public List<string>? Payments { get; set; }

    [JsonPropertyName("planning")]
    public List<string>? Planning { get; set; }
}
