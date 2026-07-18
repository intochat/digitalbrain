using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsImageDTO
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }
}
