using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsUserDTO
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("local_guide_level")]
    public int? LocalGuideLevel { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }
}
